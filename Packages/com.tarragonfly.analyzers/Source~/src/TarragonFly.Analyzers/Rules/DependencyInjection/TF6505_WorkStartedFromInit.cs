using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6505: async work, a coroutine or a scene load started from an Init(args) Init(...) method, as
    // in "protected override void Init(IAudioService audio) { _audio = audio; PlayIntroAsync().Forget(); }".
    //
    // Init runs while Init(args) is still wiring the scene: before Awake on many objects, sometimes in
    // the editor for a preloaded scene, and again on every Play Mode entry with domain reload off.
    // Work started there runs against services and objects that may not be ready, and an exception
    // in it stops the remaining injection. Keep Init to storing its arguments, and start the work
    // from OnEnable or Start.
    //
    // Counted in the Init method's own code: calls that return UniTask, UniTaskVoid, Task or
    // ValueTask, UniTask.Void, StartCoroutine, SceneManager.LoadScene and SceneManager's async scene
    // calls, and Init itself being async. Lambdas and local functions only run when something calls
    // them later, so their bodies aren't checked. Reported on the call, or on the async keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WorkStartedFromInitAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6505",
            "Work started from Init(...)",
            "{0}; start the work from OnEnable or Start, since Init(args) is still setting up the scene when Init runs",
            "InitArgs",
            CodeKind.All);

        private static readonly HashSet<string> SceneLoads = new HashSet<string> { "LoadScene", "LoadSceneAsync", "UnloadSceneAsync" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (declaration.Identifier.ValueText != "Init") return;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method) || !Frameworks.InitArgs.IsInitMethod(method)) return;

                SyntaxToken async = declaration.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.AsyncKeyword));
                if (async.IsKind(SyntaxKind.AsyncKeyword)) context.ReportDiagnostic(Diagnostic.Create(Rule, async.GetLocation(), "An async Init keeps running after Init(args) moves on"));

                SyntaxNode body = (SyntaxNode)declaration.Body ?? declaration.ExpressionBody;
                if (body == null) return;
                foreach (SyntaxNode node in body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax) && !(n is LocalFunctionStatementSyntax)))
                {
                    if (!(node is InvocationExpressionSyntax invocation)) continue;
                    if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called) || !StartsWork(called)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), $"'{called.Name}' starts work from Init"));
                }
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool StartsWork(IMethodSymbol method)
        {
            if (UniTask.IsAwaitable(method.ReturnType)) return true;
            if (method.Name == "StartCoroutine" && SymbolNames.IsOrDerivesFrom(method.ContainingType, "UnityEngine.MonoBehaviour")) return true;
            if (method.Name == "Void" && SymbolNames.IsNamed(method.ContainingType, "Cysharp.Threading.Tasks.UniTask")) return true;
            return SceneLoads.Contains(method.Name) && SymbolNames.IsNamed(method.ContainingType, "UnityEngine.SceneManagement.SceneManager");
        }
    }
}