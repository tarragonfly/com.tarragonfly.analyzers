using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5003: waiting for a task by blocking, as in "loadTask.Wait()", "loadTask.Result" or
    // "loadTask.GetAwaiter().GetResult()".
    //
    // On Unity's main thread this freezes the game until the task is done, and when the task needs
    // the main thread to continue (most Unity APIs, UniTask's PlayerLoop) it never finishes: the
    // editor hangs. Await the task instead.
    //
    // Checked: Task.Wait, Task.WaitAll, Task.WaitAny, Task<T>.Result and GetAwaiter().GetResult() on
    // Task and Task<T>, anywhere except a Main method. Result isn't reported where the task is already
    // finished: after an earlier Wait() or Wait(CancellationToken) on it in the same block or one
    // around it, in the then-branch of "if (task.Wait(100))", and on the task a ContinueWith callback
    // receives, as long as nothing assigns the task in between. Nor is nameof(Task<T>.Result). A timed
    // Wait on its own, as in "task.Wait(0);", may give up before the task is done, so a Result after
    // it is still reported. Reported on the call or the Result access. Game code only.
    //
    // GetAwaiter().GetResult() on UniTask and UniTask<T> counts too.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BlockingOnTaskAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5003",
            "Blocking wait on a task",
            "'{0}' blocks until the task finishes, which freezes the game and can hang it for good on the main thread; await the task instead",
            "Async",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || InMain(invocation)) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;

                bool waits = (method.Name == "Wait" || method.Name == "WaitAll" || method.Name == "WaitAny") && IsTask(method.ContainingType);
                bool getsResult = method.Name == "GetResult" && access.Expression is InvocationExpressionSyntax awaiter
                    && awaiter.Expression is MemberAccessExpressionSyntax awaiterAccess && awaiterAccess.Name.Identifier.ValueText == "GetAwaiter"
                    && IsBlockable(context.SemanticModel.GetTypeInfo(awaiterAccess.Expression, context.CancellationToken).Type);
                if (waits || getsResult) context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), access.Name.Identifier.ValueText));
            }), SyntaxKind.InvocationExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MemberAccessExpressionSyntax access = (MemberAccessExpressionSyntax)context.Node;
                if (access.Name.Identifier.ValueText != "Result" || InMain(access) || NameOfExpressions.IsInsideNameOf(access)) return;
                if (!(context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol is IPropertySymbol property) || !IsTask(property.ContainingType)) return;
                ISymbol task = context.SemanticModel.GetSymbolInfo(access.Expression, context.CancellationToken).Symbol;
                if (task != null && (IsContinuationArgument(context, access, task) || WaitedBefore(context, access, task) || InThenBranchOfWait(context, access, task))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.GetLocation(), "Result"));
            }), SyntaxKind.SimpleMemberAccessExpression);
        }

        // The parameter of a lambda passed to ContinueWith: the task that just finished, unless the
        // lambda assigns the parameter before reading the Result.
        private static bool IsContinuationArgument(SyntaxNodeAnalysisContext context, SyntaxNode result, ISymbol task)
        {
            if (!(task is IParameterSymbol parameter) || !(parameter.ContainingSymbol is IMethodSymbol lambda) || lambda.MethodKind != MethodKind.AnonymousFunction) return false;
            AnonymousFunctionExpressionSyntax function = parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken).FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
            if (!(function?.Parent is ArgumentSyntax argument) || !(argument.Parent?.Parent is InvocationExpressionSyntax call)) return false;
            if (!(call.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "ContinueWith") return false;
            return !AssignedBetween(context, function, task, function.SpanStart, result.SpanStart);
        }

        // An earlier statement, in the block holding the Result or one around it, calls Wait() or
        // Wait(CancellationToken) on the same task, and nothing assigns the task after it.
        private static bool WaitedBefore(SyntaxNodeAnalysisContext context, SyntaxNode result, ISymbol task)
        {
            for (SyntaxNode node = result; node.Parent != null && !(node is MemberDeclarationSyntax); node = node.Parent)
            {
                if (!(node.Parent is BlockSyntax block) || !(node is StatementSyntax statement)) continue;
                foreach (StatementSyntax earlier in block.Statements.TakeWhile(candidate => candidate != statement))
                {
                    if (earlier is ExpressionStatementSyntax expression && expression.Expression is InvocationExpressionSyntax call
                        && WaitOn(context, call, task)?.ReturnsVoid == true && !AssignedBetween(context, block, task, earlier.Span.End, result.SpanStart))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // "if (task.Wait(100)) { return task.Result; }": a timed Wait that said the task finished, read
        // in the plain then-branch before anything assigns the task.
        private static bool InThenBranchOfWait(SyntaxNodeAnalysisContext context, SyntaxNode result, ISymbol task)
        {
            for (SyntaxNode node = result; node.Parent != null && !(node is MemberDeclarationSyntax); node = node.Parent)
            {
                if (!(node.Parent is IfStatementSyntax check) || check.Statement != node || !(check.Condition is InvocationExpressionSyntax call)) continue;
                if (WaitOn(context, call, task) is IMethodSymbol wait && !wait.ReturnsVoid && !AssignedBetween(context, check, task, check.Condition.Span.End, result.SpanStart)) return true;
            }
            return false;
        }

        // The Task.Wait overload called on the task, or null. Wait() and Wait(CancellationToken) return
        // nothing and only come back once the task is done; the timed ones return whether it finished.
        private static IMethodSymbol WaitOn(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax call, ISymbol task)
        {
            if (!(call.Expression is MemberAccessExpressionSyntax wait) || wait.Name.Identifier.ValueText != "Wait") return null;
            if (!SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(wait.Expression, context.CancellationToken).Symbol, task)) return null;
            return context.SemanticModel.GetSymbolInfo(call, context.CancellationToken).Symbol is IMethodSymbol method && IsTask(method.ContainingType) ? method : null;
        }

        // Whether code inside scope, between the two positions, assigns the task or passes it by ref or out.
        private static bool AssignedBetween(SyntaxNodeAnalysisContext context, SyntaxNode scope, ISymbol task, int from, int to)
        {
            foreach (SyntaxNode node in scope.DescendantNodes())
            {
                if (node.SpanStart < from || node.Span.End > to) continue;
                ExpressionSyntax target = node is AssignmentExpressionSyntax assignment ? assignment.Left
                    : node is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None) ? argument.Expression : null;
                if (target != null && SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(target, context.CancellationToken).Symbol, task)) return true;
            }
            return false;
        }

        private static bool IsTask(ITypeSymbol type)
        {
            return SymbolNames.IsNamed(type?.OriginalDefinition, "System.Threading.Tasks.Task") || SymbolNames.IsNamed(type?.OriginalDefinition, "System.Threading.Tasks.Task`1");
        }

        private static bool IsBlockable(ITypeSymbol type)
        {
            return IsTask(type) || SymbolNames.IsNamed(type?.OriginalDefinition, "Cysharp.Threading.Tasks.UniTask")
                || SymbolNames.IsNamed(type?.OriginalDefinition, "Cysharp.Threading.Tasks.UniTask`1");
        }

        private static bool InMain(SyntaxNode node)
        {
            MethodDeclarationSyntax method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            return method != null && method.Identifier.ValueText == "Main" && method.Modifiers.Any(SyntaxKind.StaticKeyword);
        }
    }
}