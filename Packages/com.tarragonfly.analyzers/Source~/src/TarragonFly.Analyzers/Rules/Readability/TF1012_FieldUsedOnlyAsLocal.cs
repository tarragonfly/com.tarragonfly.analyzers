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
    // a write on every path (an out argument counts as a write, as does a value set with ?:, ??, ?. or
    // an object initializer), and something reads it. A read that may come first, a write only on some
    // paths, an initializer, a ++ or += before any write, and a use through another object rule it
    // out. So does a member that reads it and runs another member that uses it, by a call, a property
    // or a method group, directly or through other members or itself: the value it reads may come from
    // there, as in Run() { value = 1; Reset(); return value; } when Reset() sets it too. Subscribing a
    // method to an event doesn't count, since the handler runs later. Lambdas and local functions are
    // checked the same way on their own, and
    // their writes don't count for the code around them. Fields with attributes, such as
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
                ConcurrentBag<MemberUses> members = new ConcurrentBag<MemberUses>();

                symbolStart.RegisterOperationBlockAction(block =>
                {
                    MemberUses member = new MemberUses(MemberOf(block.OwningSymbol));
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
                        member.Add(root, type, candidates);
                    }
                    members.Add(member);
                });

                symbolStart.RegisterSymbolEndAction(end =>
                {
                    RuleOutValuesFromOtherMembers(members.ToList(), ruledOut);
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

        // Accessors stand for their property or event, so a getter counts wherever the property is used.
        private static ISymbol MemberOf(ISymbol symbol)
        {
            if (!(symbol is IMethodSymbol method)) return symbol.OriginalDefinition;
            method = (method.ReducedFrom ?? method).OriginalDefinition;
            method = method.PartialDefinitionPart ?? method;
            return method.AssociatedSymbol ?? method;
        }

        // A member that reads a field may read the value another member it runs left there, as in
        // Run() { value = 1; Reset(); return value; } when Reset() sets it to 2. A local would change
        // what Run returns.
        private static void RuleOutValuesFromOtherMembers(List<MemberUses> members, ConcurrentDictionary<IFieldSymbol, bool> ruledOut)
        {
            Dictionary<ISymbol, List<MemberUses>> parts = new Dictionary<ISymbol, List<MemberUses>>(SymbolEqualityComparer.Default);
            foreach (MemberUses member in members)
            {
                if (!parts.TryGetValue(member.Member, out List<MemberUses> list)) parts[member.Member] = list = new List<MemberUses>();
                list.Add(member);
            }

            foreach (MemberUses member in members)
            {
                if (member.Reads.Count == 0) continue;
                HashSet<ISymbol> reached = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                Stack<ISymbol> pending = new Stack<ISymbol>(member.Runs);
                while (pending.Count > 0)
                {
                    ISymbol next = pending.Pop();
                    if (!reached.Add(next) || !parts.TryGetValue(next, out List<MemberUses> list)) continue;
                    foreach (MemberUses part in list)
                    {
                        foreach (IFieldSymbol field in part.Uses.Where(member.Reads.Contains)) ruledOut[field] = true;
                        foreach (ISymbol run in part.Runs) pending.Push(run);
                    }
                }
            }
        }

        // The candidate fields one member reads and uses, and the members of its own type it runs
        // through a call, a property or a method group.
        private sealed class MemberUses
        {
            public readonly ISymbol Member;
            public readonly HashSet<IFieldSymbol> Reads = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
            public readonly HashSet<IFieldSymbol> Uses = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
            public readonly HashSet<ISymbol> Runs = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            public MemberUses(ISymbol member)
            {
                Member = member;
            }

            public void Add(IOperation root, INamedTypeSymbol type, ImmutableArray<IFieldSymbol> candidates)
            {
                foreach (IOperation operation in root.DescendantsAndSelf())
                {
                    switch (operation)
                    {
                        case IFieldReferenceOperation reference when candidates.Contains(reference.Field.OriginalDefinition, SymbolEqualityComparer.Default):
                            Uses.Add(reference.Field.OriginalDefinition);
                            if (!IsWrite(reference)) Reads.Add(reference.Field.OriginalDefinition);
                            break;
                        case IInvocationOperation invocation:
                            AddRun(invocation.TargetMethod, type);
                            break;
                        case IPropertyReferenceOperation property:
                            AddRun(property.Property, type);
                            break;
                        case IMethodReferenceOperation method when !(method.Parent?.Parent is IEventAssignmentOperation):
                            AddRun(method.Method, type);
                            break;
                    }
                }
            }

            private void AddRun(ISymbol run, INamedTypeSymbol type)
            {
                if (SymbolEqualityComparer.Default.Equals(run.ContainingType?.OriginalDefinition, type)) Runs.Add(MemberOf(run));
            }

            private static bool IsWrite(IFieldReferenceOperation reference)
            {
                if (reference.Parent is ISimpleAssignmentOperation assignment) return assignment.Target == reference;
                return reference.Parent is IArgumentOperation argument && argument.Parameter?.RefKind == RefKind.Out;
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

            // Captures the graph assigns a candidate through. Before a value with ?:, ??, ?. or an
            // object initializer, the graph captures the field being set, then assigns to the capture.
            private readonly Dictionary<CaptureId, int> assignedCaptures = new Dictionary<CaptureId, int>();
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
                FindAssignedCaptures();
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

            private void FindAssignedCaptures()
            {
                HashSet<CaptureId> targets = new HashSet<CaptureId>();
                foreach (IOperation operation in graph.Blocks.SelectMany(BlockOperations).SelectMany(root => root.DescendantsAndSelf()))
                {
                    if (WriteTarget(operation) is IFlowCaptureReferenceOperation reference) targets.Add(reference.Id);
                }
                foreach (IOperation root in graph.Blocks.SelectMany(BlockOperations))
                {
                    if (root is IFlowCaptureOperation capture && targets.Contains(capture.Id) && Index(capture.Value) is int field) assignedCaptures[capture.Id] = field;
                }
            }

            private static IEnumerable<IOperation> BlockOperations(BasicBlock block)
            {
                return block.BranchValue == null ? block.Operations : block.Operations.Add(block.BranchValue);
            }

            // What an assignment, a ++ or --, or a ref or out argument writes to.
            private static IOperation WriteTarget(IOperation operation)
            {
                switch (operation)
                {
                    case IAssignmentOperation assignment: return assignment.Target;
                    case IIncrementOrDecrementOperation step: return step.Target;
                    case IArgumentOperation argument when argument.Parameter != null && argument.Parameter.RefKind != RefKind.None: return argument.Value;
                    default: return null;
                }
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
                    case IFlowCaptureOperation capture when assignedCaptures.ContainsKey(capture.Id):
                        return;
                    case ISimpleAssignmentOperation assignment when TargetIndex(assignment.Target) is int written:
                        Walk(assignment.Value, state);
                        state[written] = true;
                        return;
                    case ICompoundAssignmentOperation compound when TargetIndex(compound.Target) is int updated:
                        Read(updated, state);
                        Walk(compound.Value, state);
                        state[updated] = true;
                        return;
                    case IIncrementOrDecrementOperation step when TargetIndex(step.Target) is int stepped:
                        Read(stepped, state);
                        state[stepped] = true;
                        return;
                    case IArgumentOperation argument when TargetIndex(argument.Value) is int passed:
                        if (argument.Parameter?.RefKind != RefKind.Out) Read(passed, state);
                        state[passed] = true;
                        return;
                    case IFieldReferenceOperation reference when Index(reference) is int used:
                        Read(used, state);
                        return;
                    case IFlowCaptureReferenceOperation reference when assignedCaptures.TryGetValue(reference.Id, out int captured):
                        Read(captured, state);
                        return;
                    case IFlowAnonymousFunctionOperation lambda:
                        if (reporting) new FieldFlow(candidates, graph.GetAnonymousFunctionControlFlowGraph(lambda), ruledOut, read).Run();
                        return;
                }
                foreach (IOperation child in operation.ChildOperations) Walk(child, state);
            }

            // The candidate a target names, directly or through a capture of it.
            private int? TargetIndex(IOperation target)
            {
                if (target is IFlowCaptureReferenceOperation reference) return assignedCaptures.TryGetValue(reference.Id, out int captured) ? captured : (int?)null;
                return Index(target);
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