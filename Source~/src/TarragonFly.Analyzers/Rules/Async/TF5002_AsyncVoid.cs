using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5002: an "async void" method.
    //
    // Callers can't await it or know when it finished, and an exception thrown after the first await
    // bypasses every try/catch around the call. In Unity, return UniTask, or UniTaskVoid with
    // .Forget() for fire-and-forget work, including async Unity messages such as Start.
    //
    // Event handlers are fine: methods with the (object sender, EventArgs e) shape, and methods the
    // same type subscribes to an event with +=. So are overrides and interface implementations, whose
    // signature is fixed elsewhere. Lambdas and local functions aren't checked. Reported on "void".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AsyncVoidAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5002",
            "Async void method",
            "Callers can't await '{0}' or catch its exceptions; return UniTask, or UniTaskVoid and call .Forget()",
            "Async",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!declaration.Modifiers.Any(SyntaxKind.AsyncKeyword) || !(declaration.ReturnType is PredefinedTypeSyntax returnType) || !returnType.Keyword.IsKind(SyntaxKind.VoidKeyword)) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                if (BaseMembers.IsOverrideOrImplementation(method) || HasEventHandlerShape(method) || IsSubscribedToAnEvent(context, method)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, returnType.GetLocation(), method.Name));
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool HasEventHandlerShape(IMethodSymbol method)
        {
            return method.Parameters.Length == 2
                && method.Parameters[0].Type.SpecialType == SpecialType.System_Object
                && SymbolNames.IsOrDerivesFrom(method.Parameters[1].Type, "System.EventArgs");
        }

        // "someEvent += Handler" anywhere in the declaring type.
        private static bool IsSubscribedToAnEvent(SyntaxNodeAnalysisContext context, IMethodSymbol method)
        {
            foreach (SyntaxReference part in method.ContainingType.DeclaringSyntaxReferences)
            {
                if (part.SyntaxTree != context.Node.SyntaxTree) continue;
                foreach (SyntaxNode node in part.GetSyntax(context.CancellationToken).DescendantNodes())
                {
                    if (!(node is AssignmentExpressionSyntax assignment) || !assignment.IsKind(SyntaxKind.AddAssignmentExpression)) continue;
                    if (!(context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is IEventSymbol)) continue;
                    if (SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(assignment.Right, context.CancellationToken).Symbol, method)) return true;
                }
            }
            return false;
        }
    }
}