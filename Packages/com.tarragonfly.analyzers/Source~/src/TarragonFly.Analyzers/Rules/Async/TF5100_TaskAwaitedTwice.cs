using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5100: a UniTask or ValueTask in a local or parameter used up twice, as in
    // "UniTask fade = FadeAsync(ct); await fade; await fade;".
    //
    // A UniTask can only be awaited once: its source is returned to a pool after the first await, so
    // the second await throws, or worse, waits on whatever reused the source. ValueTask has the same
    // limit. Call .Preserve() when the task really has to be awaited more than once.
    //
    // Using up a task means awaiting it, calling one of its own methods, such as GetAwaiter(),
    // AsUniTask() or AsTask(), which read the same pooled source, or passing it to a method, which
    // covers Forget(), ToCoroutine(), AttachExternalCancellation() and UniTask.WhenAll(...).
    // Preserve() and object's ToString, Equals, GetHashCode and GetType are the exceptions. A second use counts when the control flow graph can reach it from the first
    // without the variable being assigned again, which includes one await inside a loop around a task
    // created before the loop. A variable assigned from Preserve() anywhere is skipped. Uses in
    // different branches of an if or switch are fine. Fields aren't checked, and neither are uses the
    // compiler moves into temporaries, such as inside ?? or ?: operands. Reported on the second use.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TaskAwaitedTwiceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5100",
            "UniTask awaited more than once",
            "'{0}' was already awaited or passed on, and a UniTask or ValueTask can only be used once; call .Preserve() if it has to be awaited again",
            "Async",
            CodeKind.All);

        private static readonly string[] SingleUseTypes =
        {
            "Cysharp.Threading.Tasks.UniTask",
            "Cysharp.Threading.Tasks.UniTask`1",
            "System.Threading.Tasks.ValueTask",
            "System.Threading.Tasks.ValueTask`1"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterOperationAction(context =>
            {
                if (!scope.IsAnalyzed(context.Operation.Syntax.SyntaxTree)) return;
                if (!HasSingleUseVariable(context.Operation)) return;
                ControlFlowGraph graph = context.Operation is IMethodBodyOperation method
                    ? ControlFlowGraph.Create(method, context.CancellationToken)
                    : ControlFlowGraph.Create((IConstructorBodyOperation)context.Operation, context.CancellationToken);
                HashSet<SyntaxNode> reported = new HashSet<SyntaxNode>();
                AnalyzeGraph(context, graph, context.ContainingSymbol as IMethodSymbol, reported);
            }, OperationKind.MethodBody, OperationKind.ConstructorBody);
        }

        private static bool HasSingleUseVariable(IOperation body)
        {
            foreach (IOperation operation in body.Descendants())
            {
                if (operation is ILocalReferenceOperation local && IsSingleUse(local.Local.Type)) return true;
                if (operation is IParameterReferenceOperation parameter && IsSingleUse(parameter.Parameter.Type)) return true;
            }
            return false;
        }

        private static bool IsSingleUse(ITypeSymbol type)
        {
            if (type == null) return false;
            foreach (string name in SingleUseTypes)
            {
                if (SymbolNames.IsNamed(type.OriginalDefinition, name)) return true;
            }
            return false;
        }

        private static void AnalyzeGraph(OperationAnalysisContext context, ControlFlowGraph graph, IMethodSymbol owner, HashSet<SyntaxNode> reported)
        {
            List<ISymbol> variables = VariablesOwnedBy(graph, owner);
            foreach (ISymbol variable in variables)
            {
                if (IsEverPreserved(graph, variable)) continue;
                foreach (IOperation use in UsesUp(graph, variable))
                {
                    IOperation next = NextUseUp(graph, variable, use);
                    if (next == null || !reported.Add(next.Syntax)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, next.Syntax.GetLocation(), variable.Name));
                }
            }

            foreach (IMethodSymbol localFunction in graph.LocalFunctions)
            {
                AnalyzeGraph(context, graph.GetLocalFunctionControlFlowGraph(localFunction, context.CancellationToken), localFunction, reported);
            }
            foreach (BasicBlock block in graph.Blocks)
            {
                foreach (IFlowAnonymousFunctionOperation lambda in OperationsOf(block).SelectMany(o => o.DescendantsAndSelf()).OfType<IFlowAnonymousFunctionOperation>())
                {
                    AnalyzeGraph(context, graph.GetAnonymousFunctionControlFlowGraph(lambda, context.CancellationToken), lambda.Symbol, reported);
                }
            }
        }

        // Locals declared in this graph's regions and parameters of the method it belongs to; a local a
        // lambda captures belongs to the outer graph, where its order of uses is known.
        private static List<ISymbol> VariablesOwnedBy(ControlFlowGraph graph, IMethodSymbol owner)
        {
            List<ISymbol> variables = new List<ISymbol>();
            Stack<ControlFlowRegion> regions = new Stack<ControlFlowRegion>();
            regions.Push(graph.Root);
            while (regions.Count > 0)
            {
                ControlFlowRegion region = regions.Pop();
                variables.AddRange(region.Locals.Where(l => IsSingleUse(l.Type)));
                foreach (ControlFlowRegion nested in region.NestedRegions) regions.Push(nested);
            }
            if (owner != null) variables.AddRange(owner.Parameters.Where(p => IsSingleUse(p.Type) && p.RefKind == RefKind.None));
            return variables;
        }

        private static IEnumerable<IOperation> OperationsOf(BasicBlock block)
        {
            foreach (IOperation operation in block.Operations) yield return operation;
            if (block.BranchValue != null) yield return block.BranchValue;
        }

        private static IEnumerable<IOperation> UsesUp(ControlFlowGraph graph, ISymbol variable)
        {
            foreach (BasicBlock block in graph.Blocks)
            {
                foreach (IOperation operation in OperationsOf(block))
                {
                    foreach (IOperation use in UsesUpIn(operation, variable)) yield return use;
                }
            }
        }

        // References that use the task up, in evaluation order; lambda bodies are separate graphs.
        private static IEnumerable<IOperation> UsesUpIn(IOperation operation, ISymbol variable)
        {
            foreach (IOperation reference in operation.DescendantsAndSelf())
            {
                if (References(reference, variable) && IsUsedUp(reference)) yield return reference;
            }
        }

        private static bool References(IOperation operation, ISymbol variable)
        {
            ISymbol symbol = operation is ILocalReferenceOperation local ? local.Local
                : operation is IParameterReferenceOperation parameter ? (ISymbol)parameter.Parameter : null;
            return SymbolEqualityComparer.Default.Equals(symbol, variable);
        }

        private static bool IsUsedUp(IOperation reference)
        {
            IOperation parent = reference.Parent;
            while (parent is IConversionOperation || parent is IArrayInitializerOperation || parent is IArrayCreationOperation) parent = parent.Parent;
            switch (parent)
            {
                case IAwaitOperation _:
                    return true;
                case IInvocationOperation call when call.Instance == reference:
                    return !IsObjectMethod(call.TargetMethod) && !UniTask.IsPreserve(call.TargetMethod);
                case IArgumentOperation argument when argument.Parameter?.RefKind == RefKind.None:
                    return !(argument.Parent is IInvocationOperation invocation && UniTask.IsPreserve(invocation.TargetMethod));
                default:
                    return false;
            }
        }

        // ToString, Equals, GetHashCode and GetType don't touch the task's source.
        private static bool IsObjectMethod(IMethodSymbol method)
        {
            for (IMethodSymbol current = method; current != null; current = current.OverriddenMethod)
            {
                if (current.ContainingType?.SpecialType == SpecialType.System_Object || current.ContainingType?.SpecialType == SpecialType.System_ValueType) return true;
            }
            return false;
        }

        private static bool Writes(IOperation operation, ISymbol variable, HashSet<CaptureId> captures)
        {
            foreach (IOperation node in operation.DescendantsAndSelf())
            {
                if (node is IAssignmentOperation assignment && IsVariableOrItsCapture(assignment.Target, variable, captures)) return true;
                if (node is IArgumentOperation argument && argument.Parameter?.RefKind != RefKind.None && References(argument.Value, variable)) return true;
            }
            return false;
        }

        // When the right side of "playing = PlayAsync(new List<T> { ... })" is split into several
        // operations, the graph captures the target first and assigns through the capture. A capture
        // that can hold another variable too, as in "(replace ? ref first : ref second) = default",
        // doesn't count, since the assignment may not touch this one.
        private static HashSet<CaptureId> CapturesOf(ControlFlowGraph graph, ISymbol variable)
        {
            HashSet<CaptureId> captures = new HashSet<CaptureId>();
            HashSet<CaptureId> capturesOfOtherValues = new HashSet<CaptureId>();
            foreach (BasicBlock block in graph.Blocks)
            {
                foreach (IFlowCaptureOperation capture in OperationsOf(block).SelectMany(o => o.DescendantsAndSelf()).OfType<IFlowCaptureOperation>())
                {
                    if (References(capture.Value, variable)) captures.Add(capture.Id);
                    else capturesOfOtherValues.Add(capture.Id);
                }
            }
            captures.ExceptWith(capturesOfOtherValues);
            return captures;
        }

        private static bool IsVariableOrItsCapture(IOperation target, ISymbol variable, HashSet<CaptureId> captures)
        {
            return References(target, variable) || (target is IFlowCaptureReferenceOperation capture && captures.Contains(capture.Id));
        }

        private static bool IsEverPreserved(ControlFlowGraph graph, ISymbol variable)
        {
            foreach (BasicBlock block in graph.Blocks)
            {
                foreach (IOperation operation in OperationsOf(block))
                {
                    foreach (ISimpleAssignmentOperation assignment in operation.DescendantsAndSelf().OfType<ISimpleAssignmentOperation>())
                    {
                        if (References(assignment.Target, variable) && assignment.Value.DescendantsAndSelf().OfType<IInvocationOperation>().Any(i => UniTask.IsPreserve(i.TargetMethod))) return true;
                    }
                }
            }
            return false;
        }

        // The next use the graph can reach from this one with no assignment in between, or null.
        private static IOperation NextUseUp(ControlFlowGraph graph, ISymbol variable, IOperation first)
        {
            BasicBlock startBlock = null;
            int startIndex = -1;
            List<IOperation> startOperations = null;
            foreach (BasicBlock block in graph.Blocks)
            {
                List<IOperation> operations = OperationsOf(block).ToList();
                int index = operations.FindIndex(o => o.DescendantsAndSelf().Contains(first));
                if (index < 0) continue;
                startBlock = block;
                startIndex = index;
                startOperations = operations;
                break;
            }
            if (startBlock == null) return null;
            HashSet<CaptureId> captures = CapturesOf(graph, variable);

            // The rest of the operation that holds the first use, then the rest of its block.
            List<IOperation> inFirst = UsesUpIn(startOperations[startIndex], variable).ToList();
            int position = inFirst.IndexOf(first);
            if (position >= 0 && position + 1 < inFirst.Count) return inFirst[position + 1];
            if (Writes(startOperations[startIndex], variable, captures)) return null;
            for (int i = startIndex + 1; i < startOperations.Count; i++)
            {
                IOperation found = FirstUseOrStop(startOperations[i], variable, captures, out bool stop);
                if (found != null) return found;
                if (stop) return null;
            }

            HashSet<BasicBlock> visited = new HashSet<BasicBlock>();
            Queue<BasicBlock> pending = new Queue<BasicBlock>();
            EnqueueSuccessors(startBlock, pending);
            while (pending.Count > 0)
            {
                BasicBlock block = pending.Dequeue();
                if (!visited.Add(block)) continue;
                bool stopped = false;
                foreach (IOperation operation in OperationsOf(block))
                {
                    IOperation found = FirstUseOrStop(operation, variable, captures, out bool stop);
                    if (found != null) return found;
                    if (stop)
                    {
                        stopped = true;
                        break;
                    }
                }
                if (!stopped) EnqueueSuccessors(block, pending);
            }
            return null;
        }

        private static IOperation FirstUseOrStop(IOperation operation, ISymbol variable, HashSet<CaptureId> captures, out bool stop)
        {
            IOperation use = UsesUpIn(operation, variable).FirstOrDefault();
            stop = use == null && Writes(operation, variable, captures);
            return use;
        }

        private static void EnqueueSuccessors(BasicBlock block, Queue<BasicBlock> pending)
        {
            if (block.ConditionalSuccessor?.Destination != null) pending.Enqueue(block.ConditionalSuccessor.Destination);
            if (block.FallThroughSuccessor?.Destination != null) pending.Enqueue(block.FallThroughSuccessor.Destination);
        }
    }
}