using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1502: a value stored in a local or parameter that nothing reads before it is overwritten or the
    // method ends, as in "int damage = GetBaseDamage(); damage = 10;".
    //
    // The work that computed the value is wasted, and the code reads as if the value mattered;
    // often the later line was meant to use it, or the wrong variable is assigned.
    //
    // Checked: initializers, assignments, ++, --, compound assignments and ??= to locals and to
    // parameters other than ref and out, along every path through the method and through each lambda
    // and local function in it. A catch can start from any point in its try, so a value the catch or
    // the code after it may read stays live through the whole try. Not reported: a local nothing
    // ever reads that nothing but its initializer sets (an unused local, which the compiler warns
    // about; an out argument counts as setting it), initializers set to null, true, false, 0, 1, -1,
    // "", string.Empty or default and then overwritten, variables a lambda or local function shares
    // with the code around it, variables used in a catch or finally block, the values out arguments
    // and deconstructions store (a later plain assignment to an out variable is checked as usual),
    // and foreach, using and ref locals. Reported on the store.
    //
    // A compound assignment such as "a += x" reads the old value, so the store just before it isn't
    // dead. And a compound assignment whose result is never read is reported even
    // when its right side reads the same variable, as in "a += a".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeadStoreAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1502",
            "Value stored but never read",
            "The value stored in '{0}' here is never read; remove the store or use the value",
            "DeadCode",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterOperationBlockAction(context =>
            {
                foreach (IOperation root in context.OperationBlocks)
                {
                    if (!(root is IMethodBodyBaseOperation) && !(root is IBlockOperation)) continue;
                    if (!scope.IsAnalyzed(root.Syntax.SyntaxTree)) continue;
                    foreach ((ISymbol local, SyntaxNode store) in DeadStoresWithNested(context.GetControlFlowGraph(root), null))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, store.GetLocation(), local.Name));
                    }
                }
            });
        }

        private enum AccessKind
        {
            Read,
            Write
        }

        private readonly struct Access
        {
            public Access(AccessKind kind, ISymbol variable, SyntaxNode store)
            {
                Kind = kind;
                Variable = variable;
                Store = store;
            }

            public AccessKind Kind { get; }
            public ISymbol Variable { get; }

            // The syntax to report for a write that may be dead; null when the write isn't reportable.
            public SyntaxNode Store { get; }
        }

        // The dead stores of a method, lambda or local function, then those of the lambdas and local
        // functions inside it. Null for the member itself.
        private static IEnumerable<(ISymbol, SyntaxNode)> DeadStoresWithNested(ControlFlowGraph graph, IMethodSymbol function)
        {
            foreach ((ISymbol, SyntaxNode) store in DeadStores(graph, function)) yield return store;
            foreach (IMethodSymbol local in graph.LocalFunctions)
            {
                foreach ((ISymbol, SyntaxNode) store in DeadStoresWithNested(graph.GetLocalFunctionControlFlowGraph(local), local)) yield return store;
            }
            foreach (IFlowAnonymousFunctionOperation lambda in graph.Blocks.SelectMany(Operations).SelectMany(o => o.DescendantsAndSelf()).OfType<IFlowAnonymousFunctionOperation>())
            {
                foreach ((ISymbol, SyntaxNode) store in DeadStoresWithNested(graph.GetAnonymousFunctionControlFlowGraph(lambda), lambda.Symbol)) yield return store;
            }
        }

        private static List<(ISymbol, SyntaxNode)> DeadStores(ControlFlowGraph graph, IMethodSymbol function)
        {
            HashSet<ISymbol> untracked = UntrackedVariables(graph, function);
            Dictionary<CaptureId, ISymbol> targets = AssignedCaptures(graph);
            List<Access>[] accesses = graph.Blocks.Select(block => Accesses(block, targets)).ToArray();
            HashSet<ISymbol>[] liveIn = graph.Blocks.Select(_ => new HashSet<ISymbol>(SymbolEqualityComparer.Default)).ToArray();
            List<BasicBlock>[] handlers = graph.Blocks.Select(block => HandlerEntries(graph, block)).ToArray();

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = graph.Blocks.Length - 1; i >= 0; i--)
                {
                    HashSet<ISymbol> live = LiveOut(graph.Blocks[i], liveIn);
                    WalkBackward(accesses[i], live, LiveAtHandlers(handlers[i], liveIn), null);
                    if (!live.SetEquals(liveIn[i]))
                    {
                        liveIn[i] = live;
                        changed = true;
                    }
                }
            }

            List<(ISymbol Variable, SyntaxNode Store)> dead = new List<(ISymbol, SyntaxNode)>();
            for (int i = 0; i < graph.Blocks.Length; i++) WalkBackward(accesses[i], LiveOut(graph.Blocks[i], liveIn), LiveAtHandlers(handlers[i], liveIn), dead);

            HashSet<ISymbol> read = new HashSet<ISymbol>(accesses.SelectMany(list => list).Where(a => a.Kind == AccessKind.Read).Select(a => a.Variable), SymbolEqualityComparer.Default);
            Dictionary<ISymbol, int> writes = new Dictionary<ISymbol, int>(SymbolEqualityComparer.Default);
            foreach (Access access in accesses.SelectMany(list => list).Where(a => a.Kind == AccessKind.Write))
            {
                writes.TryGetValue(access.Variable, out int count);
                writes[access.Variable] = count + 1;
            }
            List<(ISymbol, SyntaxNode)> reported = new List<(ISymbol, SyntaxNode)>();
            foreach ((ISymbol variable, SyntaxNode store) in dead.OrderBy(d => d.Store.SpanStart))
            {
                if (untracked.Contains(variable)) continue;
                if (store is VariableDeclaratorSyntax declarator)
                {
                    bool unusedLocal = !read.Contains(variable) && writes[variable] == 1;
                    if (unusedLocal || IsPlaceholder(declarator.Initializer?.Value)) continue;
                }
                reported.Add((variable, store));
            }
            return reported;
        }

        private static HashSet<ISymbol> LiveOut(BasicBlock block, HashSet<ISymbol>[] liveIn)
        {
            HashSet<ISymbol> live = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (ControlFlowBranch branch in new[] { block.FallThroughSuccessor, block.ConditionalSuccessor })
            {
                if (branch?.Destination != null) live.UnionWith(liveIn[branch.Destination.Ordinal]);
            }
            return live;
        }

        // The first blocks of the catch handlers (and their filters) of every try the block is in;
        // the graph has no edges into them, since any operation in the try may throw. A lambda's or
        // local function's graph ends at its root region; the regions past it belong to the code
        // around it.
        private static List<BasicBlock> HandlerEntries(ControlFlowGraph graph, BasicBlock block)
        {
            List<BasicBlock> entries = new List<BasicBlock>();
            for (ControlFlowRegion region = block.EnclosingRegion; region != null && region.Kind != ControlFlowRegionKind.Root; region = region.EnclosingRegion)
            {
                if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion?.Kind != ControlFlowRegionKind.TryAndCatch) continue;
                foreach (ControlFlowRegion handler in region.EnclosingRegion.NestedRegions)
                {
                    if (handler != region) entries.Add(graph.Blocks[handler.FirstBlockOrdinal]);
                }
            }
            return entries;
        }

        private static HashSet<ISymbol> LiveAtHandlers(List<BasicBlock> handlers, HashSet<ISymbol>[] liveIn)
        {
            HashSet<ISymbol> live = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (BasicBlock handler in handlers) live.UnionWith(liveIn[handler.Ordinal]);
            return live;
        }

        // Walks the block's accesses from the end; a write the code after it doesn't read is dead.
        // In a try, what the catch handlers read stays live before every write, since the operations
        // before the write may throw.
        private static void WalkBackward(List<Access> accesses, HashSet<ISymbol> live, HashSet<ISymbol> liveAtHandlers, List<(ISymbol, SyntaxNode)> dead)
        {
            live.UnionWith(liveAtHandlers);
            for (int i = accesses.Count - 1; i >= 0; i--)
            {
                Access access = accesses[i];
                if (access.Kind == AccessKind.Read)
                {
                    live.Add(access.Variable);
                    continue;
                }
                if (dead != null && access.Store != null && !live.Contains(access.Variable)) dead.Add((access.Variable, access.Store));
                live.Remove(access.Variable);
                live.UnionWith(liveAtHandlers);
            }
        }

        private static List<Access> Accesses(BasicBlock block, Dictionary<CaptureId, ISymbol> targets)
        {
            List<Access> accesses = new List<Access>();
            foreach (IOperation operation in block.Operations) Walk(operation, accesses, targets);
            if (block.BranchValue != null) Walk(block.BranchValue, accesses, targets);
            return accesses;
        }

        // Where the right side of an assignment branches, as in "a = flag ? 1 : 0", the graph first
        // captures the variable being assigned and assigns the capture later. Maps those captures to
        // their variable.
        private static Dictionary<CaptureId, ISymbol> AssignedCaptures(ControlFlowGraph graph)
        {
            HashSet<CaptureId> assigned = new HashSet<CaptureId>();
            foreach (IOperation operation in graph.Blocks.SelectMany(Operations).SelectMany(o => o.DescendantsAndSelf()))
            {
                if (operation is IAssignmentOperation assignment && assignment.Target is IFlowCaptureReferenceOperation capture) assigned.Add(capture.Id);
            }
            Dictionary<CaptureId, ISymbol> targets = new Dictionary<CaptureId, ISymbol>();
            foreach (IOperation operation in graph.Blocks.SelectMany(Operations).SelectMany(o => o.DescendantsAndSelf()))
            {
                if (operation is IFlowCaptureOperation capture && assigned.Contains(capture.Id) && Variable(capture.Value) is ISymbol variable) targets[capture.Id] = variable;
            }
            return targets;
        }

        // Accesses in the order the code runs them.
        private static void Walk(IOperation operation, List<Access> accesses, Dictionary<CaptureId, ISymbol> targets)
        {
            switch (operation)
            {
                case IFlowCaptureOperation capture when targets.ContainsKey(capture.Id):
                    return;
                case IFlowCaptureReferenceOperation captured when targets.TryGetValue(captured.Id, out ISymbol variable):
                    // Outside an assignment target, as in the null check of "a ??= b", this reads the variable.
                    accesses.Add(new Access(AccessKind.Read, variable, null));
                    return;
                case ISimpleAssignmentOperation assignment when Target(assignment.Target, targets) is ISymbol target:
                    Walk(assignment.Value, accesses, targets);
                    bool unpacked = assignment.Syntax is AssignmentExpressionSyntax written && (written.Left is TupleExpressionSyntax || written.Left is DeclarationExpressionSyntax);
                    SyntaxNode store = !unpacked && (assignment.Syntax is VariableDeclaratorSyntax || assignment.Syntax is AssignmentExpressionSyntax) ? assignment.Syntax : null;
                    accesses.Add(new Access(AccessKind.Write, target, store));
                    return;
                case ICompoundAssignmentOperation compound when Target(compound.Target, targets) is ISymbol target:
                    accesses.Add(new Access(AccessKind.Read, target, null));
                    Walk(compound.Value, accesses, targets);
                    accesses.Add(new Access(AccessKind.Write, target, compound.Syntax));
                    return;
                case ICoalesceAssignmentOperation coalesce when Target(coalesce.Target, targets) is ISymbol target:
                    accesses.Add(new Access(AccessKind.Read, target, null));
                    Walk(coalesce.Value, accesses, targets);
                    accesses.Add(new Access(AccessKind.Write, target, coalesce.Syntax));
                    return;
                case IIncrementOrDecrementOperation step when Target(step.Target, targets) is ISymbol target:
                    accesses.Add(new Access(AccessKind.Read, target, null));
                    accesses.Add(new Access(AccessKind.Write, target, step.Syntax));
                    return;
                case IArgumentOperation argument when Variable(argument.Value) is ISymbol passed && argument.Parameter != null && argument.Parameter.RefKind != RefKind.None:
                    if (argument.Parameter.RefKind != RefKind.Out) accesses.Add(new Access(AccessKind.Read, passed, null));
                    accesses.Add(new Access(AccessKind.Write, passed, null));
                    return;
                case IDeconstructionAssignmentOperation deconstruction:
                    Walk(deconstruction.Value, accesses, targets);
                    foreach (IOperation target in deconstruction.Target.DescendantsAndSelf())
                    {
                        if (Variable(target) is ISymbol variable) accesses.Add(new Access(AccessKind.Write, variable, null));
                    }
                    return;
                case ILocalReferenceOperation reference:
                    accesses.Add(new Access(reference.IsDeclaration ? AccessKind.Write : AccessKind.Read, reference.Local, null));
                    return;
                case IParameterReferenceOperation parameter:
                    accesses.Add(new Access(AccessKind.Read, parameter.Parameter, null));
                    return;
                case IFlowAnonymousFunctionOperation _:
                    return;
            }
            foreach (IOperation child in operation.ChildOperations) Walk(child, accesses, targets);
        }

        private static ISymbol Target(IOperation target, Dictionary<CaptureId, ISymbol> targets)
        {
            if (target is IFlowCaptureReferenceOperation capture) return targets.TryGetValue(capture.Id, out ISymbol variable) ? variable : null;
            return Variable(target);
        }

        // The local or the by-value parameter an operation names, or null. A ref or out parameter's
        // value is seen by the caller, so writes to it aren't dead.
        private static ISymbol Variable(IOperation operation)
        {
            switch (operation)
            {
                case ILocalReferenceOperation local: return local.Local;
                case IParameterReferenceOperation parameter when parameter.Parameter.RefKind == RefKind.None: return parameter.Parameter;
                default: return null;
            }
        }

        // Variables the analysis can't follow: used by lambdas or local functions, in catch or
        // finally blocks, declared by foreach, using or as ref, or, inside a lambda or local function,
        // belonging to the code around it.
        private static HashSet<ISymbol> UntrackedVariables(ControlFlowGraph graph, IMethodSymbol function)
        {
            HashSet<ISymbol> untracked = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (BasicBlock block in graph.Blocks)
            {
                bool handler = false;
                for (ControlFlowRegion region = block.EnclosingRegion; region != null && region.Kind != ControlFlowRegionKind.Root; region = region.EnclosingRegion)
                {
                    if (region.Kind == ControlFlowRegionKind.Catch || region.Kind == ControlFlowRegionKind.Finally || region.Kind == ControlFlowRegionKind.Filter
                        || region.Kind == ControlFlowRegionKind.FilterAndHandler) handler = true;
                }
                foreach (IOperation operation in Operations(block))
                {
                    foreach (IOperation reference in operation.DescendantsAndSelf())
                    {
                        if (reference is IParameterReferenceOperation parameter && (handler || IsOuter(parameter.Parameter, function))) untracked.Add(parameter.Parameter);
                        if (!(reference is ILocalReferenceOperation localReference)) continue;
                        ILocalSymbol local = localReference.Local;
                        if (handler || local.IsRef || local.IsConst || IsForEachOrUsing(local) || IsOuter(local, function)) untracked.Add(local);
                    }
                    foreach (IFlowAnonymousFunctionOperation lambda in operation.DescendantsAndSelf().OfType<IFlowAnonymousFunctionOperation>())
                    {
                        SkipAll(graph.GetAnonymousFunctionControlFlowGraph(lambda), untracked);
                    }
                }
            }
            foreach (IMethodSymbol local in graph.LocalFunctions) SkipAll(graph.GetLocalFunctionControlFlowGraph(local), untracked);
            return untracked;
        }

        private static bool IsOuter(ISymbol variable, IMethodSymbol function)
        {
            return function != null && !SymbolEqualityComparer.Default.Equals(variable.ContainingSymbol, function);
        }

        private static void SkipAll(ControlFlowGraph inner, HashSet<ISymbol> untracked)
        {
            foreach (BasicBlock block in inner.Blocks)
            {
                foreach (IOperation operation in Operations(block))
                {
                    foreach (IOperation reference in operation.DescendantsAndSelf())
                    {
                        if (reference is ILocalReferenceOperation local) untracked.Add(local.Local);
                        if (reference is IParameterReferenceOperation parameter) untracked.Add(parameter.Parameter);
                    }
                    foreach (IFlowAnonymousFunctionOperation lambda in operation.DescendantsAndSelf().OfType<IFlowAnonymousFunctionOperation>()) SkipAll(inner.GetAnonymousFunctionControlFlowGraph(lambda), untracked);
                }
            }
            foreach (IMethodSymbol local in inner.LocalFunctions) SkipAll(inner.GetLocalFunctionControlFlowGraph(local), untracked);
        }

        private static bool IsForEachOrUsing(ILocalSymbol local)
        {
            foreach (SyntaxReference reference in local.DeclaringSyntaxReferences)
            {
                SyntaxNode declaration = reference.GetSyntax();
                if (declaration is ForEachStatementSyntax) return true;
                SyntaxNode statement = declaration.Parent?.Parent;
                if (statement is UsingStatementSyntax) return true;
                if (statement is LocalDeclarationStatementSyntax localDeclaration && !localDeclaration.UsingKeyword.IsKind(SyntaxKind.None)) return true;
            }
            return false;
        }

        private static IEnumerable<IOperation> Operations(BasicBlock block)
        {
            return block.BranchValue == null ? block.Operations : block.Operations.Concat(new[] { block.BranchValue });
        }

        // An initial value that only sets the local up for a later assignment.
        private static bool IsPlaceholder(ExpressionSyntax value)
        {
            while (value is ParenthesizedExpressionSyntax parenthesized) value = parenthesized.Expression;
            switch (value)
            {
                case null:
                    return false;
                case LiteralExpressionSyntax literal:
                    switch (literal.Kind())
                    {
                        case SyntaxKind.NullLiteralExpression:
                        case SyntaxKind.TrueLiteralExpression:
                        case SyntaxKind.FalseLiteralExpression:
                        case SyntaxKind.DefaultLiteralExpression:
                            return true;
                        case SyntaxKind.StringLiteralExpression:
                            return literal.Token.ValueText.Length == 0;
                        case SyntaxKind.NumericLiteralExpression:
                            return IsZeroOrOne(literal.Token.Value);
                        default:
                            return false;
                    }
                case PrefixUnaryExpressionSyntax negative when negative.IsKind(SyntaxKind.UnaryMinusExpression):
                    return negative.Operand is LiteralExpressionSyntax one && one.IsKind(SyntaxKind.NumericLiteralExpression) && System.Convert.ToDouble(one.Token.Value, System.Globalization.CultureInfo.InvariantCulture) == 1d;
                case DefaultExpressionSyntax _:
                    return true;
                case MemberAccessExpressionSyntax access:
                    return access.Name.Identifier.ValueText == "Empty" && access.Expression is PredefinedTypeSyntax type && type.Keyword.IsKind(SyntaxKind.StringKeyword);
                default:
                    return false;
            }
        }

        private static bool IsZeroOrOne(object value)
        {
            double number = System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return number == 0d || number == 1d;
        }
    }
}