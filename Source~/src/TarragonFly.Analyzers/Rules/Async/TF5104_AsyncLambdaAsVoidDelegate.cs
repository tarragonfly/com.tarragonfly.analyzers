using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5104: an async lambda or anonymous method turned into a delegate that returns void, as in
    // "button.clicked += async () => await SaveAsync(ct);".
    //
    // The lambda becomes async void: whoever invokes it can't wait for it, and an exception after the
    // first await skips every try/catch around the call. Wrap it with UniTask.Action(async () => ...)
    // or UniTask.UnityAction(async () => ...), which run it as UniTaskVoid and log its exceptions.
    //
    // Any delegate whose Invoke returns void counts: Action, UnityAction, UI Toolkit's EventCallback<T>
    // and C# events. Lambdas converted to Func<UniTask>, Func<Task> or similar are fine. Reported on
    // the async keyword. This is TF5002 for lambdas, which that rule doesn't check.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncLambdaAsVoidDelegateAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5104",
            "Async lambda used as a void delegate",
            "This async lambda runs as async void, so its exceptions are lost; wrap it in UniTask.Action(...) or UniTask.UnityAction(...)",
            "Async",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AnonymousFunctionExpressionSyntax lambda = (AnonymousFunctionExpressionSyntax)context.Node;
                if (!lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)) return;
                if (!(context.SemanticModel.GetSymbolInfo(lambda, context.CancellationToken).Symbol is IMethodSymbol method) || !method.ReturnsVoid) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, lambda.AsyncKeyword.GetLocation()));
            }), SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression);
        }
    }
}