using System;
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
    // Task and Task<T>, anywhere except a Main method. Result isn't reported:
    // - after an earlier statement, in the same block or switch section or one around it, that
    //   waits for the task: one that calls Wait on it or passes it to Task.WaitAll or Task.WhenAll
    //   anywhere inside it, or a "while (!task.IsCompleted)" loop without a break or goto
    // - on the task a ContinueWith callback receives
    // - inside nameof
    // GetAwaiter().GetResult() isn't reported after such a loop; after the other waits it still is.
    // Whether the wait finished isn't looked at: a Wait with a timeout, a wait on one path only and
    // a task assigned again after the wait all count. Reported on the call or the Result access.
    // Game code only.
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
                // The task in "task.GetAwaiter().GetResult()".
                ExpressionSyntax taskExpression = access.Expression is InvocationExpressionSyntax awaiter && awaiter.Expression is MemberAccessExpressionSyntax awaiterAccess
                    && awaiterAccess.Name.Identifier.ValueText == "GetAwaiter" ? awaiterAccess.Expression : null;
                bool getsResult = method.Name == "GetResult" && taskExpression != null
                    && IsBlockable(context.SemanticModel.GetTypeInfo(taskExpression, context.CancellationToken).Type);
                if (getsResult && FollowsCompletionLoop(context, invocation, taskExpression)) return;
                if (waits || getsResult) context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), access.Name.Identifier.ValueText));
            }), SyntaxKind.InvocationExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MemberAccessExpressionSyntax access = (MemberAccessExpressionSyntax)context.Node;
                if (access.Name.Identifier.ValueText != "Result" || InMain(access) || NameOfExpressions.IsInsideNameOf(access)) return;
                if (!(context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol is IPropertySymbol property) || !IsTask(property.ContainingType)) return;
                if (IsFinished(context, access, access.Expression)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.GetLocation(), "Result"));
            }), SyntaxKind.SimpleMemberAccessExpression);
        }

        // Whether the code before the Result at "use" waited for the task.
        private static bool IsFinished(SyntaxNodeAnalysisContext context, SyntaxNode use, ExpressionSyntax taskExpression)
        {
            ISymbol task = context.SemanticModel.GetSymbolInfo(taskExpression, context.CancellationToken).Symbol;
            return task != null && (IsContinuationArgument(context, task) || WaitedBefore(use, earlier => WaitsFor(context, earlier, task)));
        }

        // Whether a "while (!task.IsCompleted)" loop ran before the GetResult at "use". GetResult after
        // Wait, WaitAll or WhenAll and in a ContinueWith callback is still reported. Revisit if those
        // turn up in game code.
        private static bool FollowsCompletionLoop(SyntaxNodeAnalysisContext context, SyntaxNode use, ExpressionSyntax taskExpression)
        {
            ISymbol task = context.SemanticModel.GetSymbolInfo(taskExpression, context.CancellationToken).Symbol;
            return task != null && WaitedBefore(use, earlier => earlier is WhileStatementSyntax loop && LoopsUntilCompleted(context, loop, task));
        }

        // The parameter of a lambda passed to ContinueWith: the task that just finished.
        private static bool IsContinuationArgument(SyntaxNodeAnalysisContext context, ISymbol task)
        {
            if (!(task is IParameterSymbol parameter) || !(parameter.ContainingSymbol is IMethodSymbol lambda) || lambda.MethodKind != MethodKind.AnonymousFunction) return false;
            AnonymousFunctionExpressionSyntax function = parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken).FirstAncestorOrSelf<AnonymousFunctionExpressionSyntax>();
            if (!(function?.Parent is ArgumentSyntax argument) || !(argument.Parent?.Parent is InvocationExpressionSyntax call)) return false;
            return call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "ContinueWith";
        }

        // Whether a statement before the one holding the use, in its block or switch section or one
        // around it, waits.
        private static bool WaitedBefore(SyntaxNode use, Func<StatementSyntax, bool> waits)
        {
            for (SyntaxNode node = use; node.Parent != null && !(node is MemberDeclarationSyntax); node = node.Parent)
            {
                if (!(node is StatementSyntax statement)) continue;
                SyntaxList<StatementSyntax> statements;
                if (node.Parent is BlockSyntax block) statements = block.Statements;
                else if (node.Parent is SwitchSectionSyntax section) statements = section.Statements;
                else continue;

                if (statements.TakeWhile(candidate => candidate != statement).Any(waits)) return true;
            }
            return false;
        }

        // Whether the statement waits for the task: a "while (!task.IsCompleted)" loop, or a statement
        // that calls Wait on the task or passes it to Task.WaitAll or Task.WhenAll anywhere inside it,
        // as in "task.Wait(100);", "if (wait) await Task.WhenAll(task, other);" or a lambda that waits.
        // Whether that wait ran or finished isn't looked at. Revisit if a Result after a wait that
        // didn't finish turns up in game code.
        private static bool WaitsFor(SyntaxNodeAnalysisContext context, StatementSyntax statement, ISymbol task)
        {
            if (statement is WhileStatementSyntax loop && LoopsUntilCompleted(context, loop, task)) return true;
            return statement.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => IsWaitOn(context, call, task) || IsWhenAllOrWaitAllWith(context, call, task));
        }

        // "task.Wait()" with any arguments.
        private static bool IsWaitOn(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax call, ISymbol task)
        {
            if (!(call.Expression is MemberAccessExpressionSyntax wait) || wait.Name.Identifier.ValueText != "Wait") return false;
            if (!SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(wait.Expression, context.CancellationToken).Symbol, task)) return false;
            return context.SemanticModel.GetSymbolInfo(call, context.CancellationToken).Symbol is IMethodSymbol method && IsTask(method.ContainingType);
        }

        // "Task.WaitAll(task, other)" or "Task.WhenAll(task, other)" with the task as one of the
        // arguments. Tasks passed in an array aren't followed.
        private static bool IsWhenAllOrWaitAllWith(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax call, ISymbol task)
        {
            if (!(call.Expression is MemberAccessExpressionSyntax access)) return false;
            string name = access.Name.Identifier.ValueText;
            if (name != "WhenAll" && name != "WaitAll") return false;
            if (!(context.SemanticModel.GetSymbolInfo(call, context.CancellationToken).Symbol is IMethodSymbol method) || !IsTask(method.ContainingType)) return false;
            return call.ArgumentList.Arguments.Any(argument => SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(argument.Expression, context.CancellationToken).Symbol, task));
        }

        // "while (!task.IsCompleted) { ... }" that can only end through its condition. A break or goto
        // anywhere inside counts as another way out, also when it belongs to an inner loop or switch.
        private static bool LoopsUntilCompleted(SyntaxNodeAnalysisContext context, WhileStatementSyntax loop, ISymbol task)
        {
            if (!(loop.Condition is PrefixUnaryExpressionSyntax not) || !not.IsKind(SyntaxKind.LogicalNotExpression)) return false;
            if (!(not.Operand is MemberAccessExpressionSyntax completed) || completed.Name.Identifier.ValueText != "IsCompleted") return false;
            if (!SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(completed.Expression, context.CancellationToken).Symbol, task)) return false;
            return !loop.Statement.DescendantNodesAndSelf().Any(node => node is BreakStatementSyntax || node is GotoStatementSyntax);
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
