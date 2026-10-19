using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2016: a method or property that can never finish: an endless loop with no way out, as in "while
    // (true) { Move(); }", or a call back into itself on every path, as in "public int Health =>
    // Health;".
    //
    // The first freezes the game for good, the second throws StackOverflowException, which Unity
    // can't catch and which crashes the editor.
    //
    // A member never finishes when no path from its start reaches a return, the end of the code or a
    // throw; for recursion, paths through a call to itself on the same object don't count. A property
    // counts as calling itself when its getter reads it or its setter sets it. Reported on the member
    // name, or on the accessor keyword.
    //
    // Endless loops in iterators and async methods aren't reported. A Unity coroutine "while (true) {
    // ...; yield return null; }" runs until StopCoroutine or the object is destroyed, and an async loop
    // ends when an awaited call is cancelled.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NeverFinishesAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2016",
            "Endless loop or recursion",
            "'{0}' can never finish: {1}",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterOperationBlockAction(context =>
            {
                if (!(context.OwningSymbol is IMethodSymbol method) || !TryGetLocation(method, out Location location, out string name)) return;
                if (!scope.IsAnalyzed(location.SourceTree)) return;
                foreach (IOperation root in context.OperationBlocks)
                {
                    if (!(root is IMethodBodyBaseOperation) && !(root is IBlockOperation)) continue;
                    ControlFlowGraph graph = context.GetControlFlowGraph(root);
                    if (!Finishes(graph, null))
                    {
                        if (method.IsAsync || IsIterator(root)) return;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, "no path leaves its loop"));
                        return;
                    }
                    if (!Finishes(graph, method))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, location, name, "every path calls it again"));
                        return;
                    }
                }
            });
        }

        private static bool TryGetLocation(IMethodSymbol method, out Location location, out string name)
        {
            location = null;
            name = method.AssociatedSymbol?.Name ?? method.Name;
            if (method.DeclaringSyntaxReferences.Length != 1) return false;
            SyntaxNode syntax = method.DeclaringSyntaxReferences[0].GetSyntax();
            switch (syntax)
            {
                case MethodDeclarationSyntax declaration:
                    location = declaration.Identifier.GetLocation();
                    return true;
                case AccessorDeclarationSyntax accessor:
                    location = accessor.Keyword.GetLocation();
                    return true;
                case ArrowExpressionClauseSyntax arrow when arrow.Parent is PropertyDeclarationSyntax property:
                    location = property.Identifier.GetLocation();
                    return true;
                case PropertyDeclarationSyntax property:
                    location = property.Identifier.GetLocation();
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsIterator(IOperation root)
        {
            return root.Syntax.DescendantNodes().Any(node => node.IsKind(SyntaxKind.YieldReturnStatement) || node.IsKind(SyntaxKind.YieldBreakStatement));
        }

        // Whether some path from the start ends the member. With self set, blocks that call the member
        // again on the same object are dead ends.
        private static bool Finishes(ControlFlowGraph graph, IMethodSymbol self)
        {
            HashSet<int> seen = new HashSet<int>();
            Stack<BasicBlock> pending = new Stack<BasicBlock>();
            pending.Push(graph.Blocks[0]);
            while (pending.Count > 0)
            {
                BasicBlock block = pending.Pop();
                if (!seen.Add(block.Ordinal)) continue;
                if (block.Kind == BasicBlockKind.Exit) return true;
                if (self != null && CallsItself(block, self)) continue;
                foreach (ControlFlowBranch branch in TakenBranches(block))
                {
                    if (branch.Semantics == ControlFlowBranchSemantics.Throw || branch.Semantics == ControlFlowBranchSemantics.Rethrow) return true;
                    if (branch.Destination != null) pending.Push(branch.Destination);
                }
            }
            return false;
        }

        // The branches control can take out of the block; with a constant condition, such as
        // while (true), only the one it always takes.
        private static IEnumerable<ControlFlowBranch> TakenBranches(BasicBlock block)
        {
            bool conditionalAlways = false;
            bool conditionalNever = false;
            if (block.ConditionalSuccessor != null && block.BranchValue != null && block.BranchValue.ConstantValue.HasValue && block.BranchValue.ConstantValue.Value is bool value)
            {
                bool taken = block.ConditionKind == ControlFlowConditionKind.WhenTrue ? value : !value;
                conditionalAlways = taken;
                conditionalNever = !taken;
            }
            if (block.FallThroughSuccessor != null && !conditionalAlways) yield return block.FallThroughSuccessor;
            if (block.ConditionalSuccessor != null && !conditionalNever) yield return block.ConditionalSuccessor;
        }

        private static bool CallsItself(BasicBlock block, IMethodSymbol self)
        {
            IEnumerable<IOperation> operations = block.BranchValue == null ? block.Operations : block.Operations.Concat(new[] { block.BranchValue });
            foreach (IOperation operation in operations.SelectMany(o => o.DescendantsAndSelf()))
            {
                switch (operation)
                {
                    case IInvocationOperation call when SameMember(call.TargetMethod, self) && OnSameObject(call.Instance):
                        return true;
                    case IPropertyReferenceOperation property when self.AssociatedSymbol is IPropertySymbol owner
                        && SymbolEqualityComparer.Default.Equals(property.Property.OriginalDefinition, owner.OriginalDefinition) && OnSameObject(property.Instance):
                        bool assigned = property.Parent is IAssignmentOperation assignment && assignment.Target == property;
                        if (self.MethodKind == MethodKind.PropertyGet ? !assigned : assigned) return true;
                        break;
                }
            }
            return false;
        }

        private static bool SameMember(IMethodSymbol called, IMethodSymbol self)
        {
            return SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, self.OriginalDefinition);
        }

        private static bool OnSameObject(IOperation instance)
        {
            return instance == null || instance is IInstanceReferenceOperation;
        }
    }
}