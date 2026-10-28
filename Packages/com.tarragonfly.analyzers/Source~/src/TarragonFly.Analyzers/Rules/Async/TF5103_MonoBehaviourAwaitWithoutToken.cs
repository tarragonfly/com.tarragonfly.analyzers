using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5103: an async method on a MonoBehaviour that awaits without any cancellation token, as in
    // "async UniTaskVoid Start() { await LoadSaveAsync(); hud.Show(); }".
    //
    // Nothing stops the method when the object is destroyed or the scene unloads, so it carries on
    // after the await and touches destroyed objects (MissingReferenceException), or shows a HUD that
    // belongs to the last scene. Pass destroyCancellationToken, or wrap an await that takes no token
    // in .AttachExternalCancellation(destroyCancellationToken).
    //
    // Checked: async instance methods declared on a MonoBehaviour that nothing else awaits, so no
    // caller can cancel them: those returning void or UniTaskVoid, and Unity messages such as
    // "async UniTask Start()", whose result Unity ignores. A method returning UniTask leaves
    // cancellation to whoever awaits it. The method also needs code that runs after one of its
    // awaits: a later statement, a loop around the await or a finally. A method that ends on its
    // await, such as "await viewManager.ShowOverlayAsync(id);" as the last line, has nothing left to
    // run on a destroyed object. Fine when
    // the method mentions any CancellationToken at all: a parameter, destroyCancellationToken,
    // GetCancellationTokenOnDestroy(), a CancellationTokenSource or a token field. Also fine when one
    // of its awaited calls could take a token, since TF5005 already reports that call with
    // destroyCancellationToken as the fix. Awaits inside lambdas and local functions belong to those.
    // Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MonoBehaviourAwaitWithoutTokenAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5103",
            "MonoBehaviour async method awaits without a cancellation token",
            "Nothing stops '{0}' when the object is destroyed; pass destroyCancellationToken, or add .AttachExternalCancellation(destroyCancellationToken) to awaits that take no token",
            "Async",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!declaration.Modifiers.Any(SyntaxKind.AsyncKeyword)) return;
                SyntaxNode body = (SyntaxNode)declaration.Body ?? declaration.ExpressionBody;
                if (body == null) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                if (method.IsStatic || !Frameworks.Unity.IsMonoBehaviour(method.ContainingType)) return;
                if (method.Parameters.Any(p => UniTask.IsCancellationToken(p.Type))) return;
                if (!IsFireAndForget(method)) return;

                List<AwaitExpressionSyntax> awaits = OwnNodes(body).OfType<AwaitExpressionSyntax>().ToList();
                if (!awaits.Any(a => HasCodeAfter(a, body)) || MentionsToken(context.SemanticModel, body, context.CancellationToken)) return;
                foreach (AwaitExpressionSyntax awaited in awaits)
                {
                    if (awaited.Expression is InvocationExpressionSyntax invocation
                        && context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation
                        && CancellationTokenNotPassedAnalyzer.MissesToken(context.SemanticModel, invocation, operation)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), method.Name));
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool IsFireAndForget(IMethodSymbol method)
        {
            return method.ReturnsVoid || UniTask.IsUniTaskVoid(method.ReturnType) || Frameworks.Unity.IsMessage(method);
        }

        // Whether the method can run more of its own code once the await finishes: a later statement in
        // any block around it, a loop around it, or a finally. A bare "return;" doesn't count.
        private static bool HasCodeAfter(AwaitExpressionSyntax awaited, SyntaxNode body)
        {
            for (SyntaxNode node = awaited; node != null && node != body; node = node.Parent)
            {
                if (node.Parent is BlockSyntax block)
                {
                    int index = block.Statements.IndexOf((StatementSyntax)node);
                    if (block.Statements.Skip(index + 1).Any(s => !(s is ReturnStatementSyntax ret && ret.Expression == null))) return true;
                }
                if (node.Parent is ForStatementSyntax || node.Parent is ForEachStatementSyntax || node.Parent is WhileStatementSyntax || node.Parent is DoStatementSyntax) return true;
                if (node.Parent is TryStatementSyntax tryStatement && tryStatement.Finally != null && node != tryStatement.Finally) return true;
            }
            return false;
        }

        // The method's own code, without lambdas and local functions.
        private static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode body)
        {
            return body.DescendantNodes(node => !(node is AnonymousFunctionExpressionSyntax) && !(node is LocalFunctionStatementSyntax));
        }

        // Any expression of type CancellationToken or CancellationTokenSource, lambdas included, since
        // a lambda that cancels the work counts too.
        private static bool MentionsToken(SemanticModel model, SyntaxNode body, CancellationToken ct)
        {
            foreach (SyntaxNode node in body.DescendantNodes())
            {
                if (!(node is IdentifierNameSyntax) && !(node is InvocationExpressionSyntax)) continue;
                ITypeSymbol type = model.GetTypeInfo(node, ct).Type;
                if (UniTask.IsCancellationToken(type) || SymbolNames.IsNamed(type, "System.Threading.CancellationTokenSource")) return true;
            }
            return false;
        }
    }
}