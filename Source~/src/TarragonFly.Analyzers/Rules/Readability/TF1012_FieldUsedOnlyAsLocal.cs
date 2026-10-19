using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1012: a private field that every method using it sets before reading, as in a "Label title;"
    // field that Bind() creates, fills and adds and nothing else touches.
    //
    // The value never needs to outlive the method, so a local says that and stops other methods
    // reading a leftover value from the last call.
    //
    // A field counts when, in every method, property or constructor that uses it, each read comes after
    // a write on every path (an out argument counts as a write), and something reads it. A read that
    // may come first, a write only on some paths, an initializer, a ++ or += before any write, and a
    // use through another object rule it out. Lambdas and local functions are checked the same way on
    // their own, and their writes don't count for the code around them. Fields with attributes, such as
    // [SerializeField], are skipped, as are fields a nested type mentions. Reported on the field name.
    // Game code only.
    //
    // Partial types are checked too, across all their parts.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldUsedOnlyAsLocalAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1012",
            "Field used only as a local",
            "Every member that uses '{0}' sets it before reading it; make it a local variable",
            "Readability",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolStartAction(symbolStart =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)symbolStart.Symbol;
                ImmutableArray<IFieldSymbol> candidates = Candidates(type, symbolStart.CancellationToken);
                if (candidates.IsEmpty) return;
                ConcurrentDictionary<IFieldSymbol, bool> ruledOut = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
                ConcurrentDictionary<IFieldSymbol, bool> read = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

                symbolStart.RegisterOperationBlockAction(block =>
                {
                    foreach (IOperation root in block.OperationBlocks)
                    {
                        if (root is IMethodBodyBaseOperation || root is IBlockOperation)
                        {
                            new FieldFlow(candidates, block.GetControlFlowGraph(root), ruledOut, read).Run();
                        }
                        else
                        {
                            foreach (IFieldSymbol field in References(root, candidates)) ruledOut[field] = true;
                        }
                    }
                });

                symbolStart.RegisterSymbolEndAction(end =>
                {
                    foreach (IFieldSymbol field in candidates)
                    {
                        if (ruledOut.ContainsKey(field) || !read.ContainsKey(field)) continue;
                        Location location = field.Locations[0];
                        if (scope.IsAnalyzed(location.SourceTree)) end.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                    }
                });
            }, SymbolKind.NamedType);
        }

        private static ImmutableArray<IFieldSymbol> Candidates(INamedTypeSymbol type, System.Threading.CancellationToken ct)
        {
            if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct) return ImmutableArray<IFieldSymbol>.Empty;
            HashSet<string> namesInNestedTypes = new HashSet<string>();
            foreach (INamedTypeSymbol nested in type.GetTypeMembers())
            {
                foreach (SyntaxReference reference in nested.DeclaringSyntaxReferences)
                {
                    foreach (IdentifierNameSyntax name in reference.GetSyntax(ct).DescendantNodes().OfType<IdentifierNameSyntax>()) namesInNestedTypes.Add(name.Identifier.ValueText);
                }
            }

            ImmutableArray<IFieldSymbol>.Builder candidates = ImmutableArray.CreateBuilder<IFieldSymbol>();
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.DeclaredAccessibility != Accessibility.Private || field.IsConst || field.IsImplicitlyDeclared || !field.GetAttributes().IsEmpty) continue;
                if (namesInNestedTypes.Contains(field.Name)) continue;
                if (field.DeclaringSyntaxReferences.Any(r => r.GetSyntax(ct) is VariableDeclaratorSyntax declarator && declarator.Initializer != null)) continue;
                candidates.Add(field);
            }
            return candidates.ToImmutable();
        }

        private static IEnumerable<IFieldSymbol> References(IOperation root, ImmutableArray<IFieldSymbol> candidates)
        {
            foreach (IOperation operation in root.DescendantsAndSelf())
            {
                if (operation is IFieldReferenceOperation reference && candidates.Contains(reference.Field.OriginalDefinition, SymbolEqualityComparer.Default)) yield return reference.Field.OriginalDefinition;
            }
        }

        // Must-write analysis over one member's control flow graph: a candidate is "set" at a point
        // when every path from the start writes it first.
        private sealed class FieldFlow
        {
            private readonly ImmutableArray<IFieldSymbol> candidates;
            private readonly ControlFlowGraph graph;
            private readonly ConcurrentDictionary<IFieldSymbol, bool> ruledOut;
            private readonly ConcurrentDictionary<IFieldSymbol, bool> read;
            private bool reporting;

            public FieldFlow(ImmutableArray<IFieldSymbol> candidates, ControlFlowGraph graph, ConcurrentDictionary<IFieldSymbol, bool> ruledOut, ConcurrentDictionary<IFieldSymbol, bool> read)
            {
                this.candidates = candidates;
                this.graph = graph;
                this.ruledOut = ruledOut;
                this.read = read;
            }

            public void Run()
            {
                int count = graph.Blocks.Length;
                bool[][] setAtEnd = new bool[count][];
                for (int i = 0; i < count; i++) setAtEnd[i] = Enumerable.Repeat(true, candidates.Length).ToArray();

                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (BasicBlock block in graph.Blocks)
                    {
                        bool[] state = Simulate(block, setAtEnd);
                        if (!state.SequenceEqual(setAtEnd[block.Ordinal]))
                        {
                            setAtEnd[block.Ordinal] = state;
                            changed = true;
                        }
                    }
                }

                reporting = true;
                foreach (BasicBlock block in graph.Blocks) Simulate(block, setAtEnd);
                foreach (IMethodSymbol local in graph.LocalFunctions) new FieldFlow(candidates, graph.GetLocalFunctionControlFlowGraph(local), ruledOut, read).Run();
            }

            private bool[] Simulate(BasicBlock block, bool[][] setAtEnd)
            {
                bool[] state = new bool[candidates.Length];
                if (block.Kind != BasicBlockKind.Entry)
                {
                    for (int i = 0; i < state.Length; i++) state[i] = true;
                    bool any = false;
                    foreach (ControlFlowBranch branch in block.Predecessors)
                    {
                        any = true;
                        bool[] before = setAtEnd[branch.Source.Ordinal];
                        for (int i = 0; i < state.Length; i++) state[i] &= before[i];
                    }
                    if (!any) for (int i = 0; i < state.Length; i++) state[i] = false;
                }
                foreach (IOperation operation in block.Operations) Walk(operation, state);
                if (block.BranchValue != null) Walk(block.BranchValue, state);
                return state;
            }

            private void Walk(IOperation operation, bool[] state)
            {
                switch (operation)
                {
                    case ISimpleAssignmentOperation assignment when Index(assignment.Target) is int written:
                        Walk(assignment.Value, state);
                        state[written] = true;
                        return;
                    case ICompoundAssignmentOperation compound when Index(compound.Target) is int updated:
                        Read(updated, state);
                        Walk(compound.Value, state);
                        state[updated] = true;
                        return;
                    case IIncrementOrDecrementOperation step when Index(step.Target) is int stepped:
                        Read(stepped, state);
                        state[stepped] = true;
                        return;
                    case IArgumentOperation argument when Index(argument.Value) is int passed:
                        if (argument.Parameter?.RefKind != RefKind.Out) Read(passed, state);
                        state[passed] = true;
                        return;
                    case IFieldReferenceOperation reference when Index(reference) is int used:
                        Read(used, state);
                        return;
                    case IFlowAnonymousFunctionOperation lambda:
                        if (reporting) new FieldFlow(candidates, graph.GetAnonymousFunctionControlFlowGraph(lambda), ruledOut, read).Run();
                        return;
                }
                foreach (IOperation child in operation.ChildOperations) Walk(child, state);
            }

            // The candidate this operation names on this object, or null. A candidate reached through
            // another object can't become a local, so it is ruled out.
            private int? Index(IOperation operation)
            {
                if (!(operation is IFieldReferenceOperation reference)) return null;
                int index = candidates.IndexOf(reference.Field.OriginalDefinition, SymbolEqualityComparer.Default);
                if (index < 0) return null;
                if (reference.Instance != null && !(reference.Instance is IInstanceReferenceOperation)) ruledOut[candidates[index]] = true;
                return index;
            }

            private void Read(int index, bool[] state)
            {
                if (!reporting) return;
                read[candidates[index]] = true;
                if (!state[index]) ruledOut[candidates[index]] = true;
            }
        }
    }
}