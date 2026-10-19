using System.Collections.Immutable;
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
    // Task and Task<T>, anywhere except a Main method. Reported on the call or the Result access. Game
    // code only.
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
                if (access.Name.Identifier.ValueText != "Result" || InMain(access)) return;
                if (context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol is IPropertySymbol property && IsTask(property.ContainingType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, access.GetLocation(), "Result"));
                }
            }), SyntaxKind.SimpleMemberAccessExpression);
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