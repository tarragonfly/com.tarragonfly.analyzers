using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2020: a ToString method that returns null.
    //
    // Callers, string interpolation and the debugger expect text; return string.Empty or a
    // description instead.
    //
    // Any method named ToString counts, overloads included. Only a literal null is reported, in a
    // return statement or as the whole expression body; "name ?? null" or "default" aren't. Lambdas
    // inside the method aren't checked. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ToStringReturnsNullAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2020",
            "ToString returns null",
            "Return string.Empty or some text from ToString, not null",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
                if (method.Identifier.ValueText != "ToString") return;
                if (method.ExpressionBody != null)
                {
                    if (method.ExpressionBody.Expression.IsKind(SyntaxKind.NullLiteralExpression)) context.ReportDiagnostic(Diagnostic.Create(Rule, method.ExpressionBody.GetLocation()));
                    return;
                }
                if (method.Body == null) return;
                foreach (SyntaxNode node in method.Body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax) && !(n is LocalFunctionStatementSyntax)))
                {
                    if (node is ReturnStatementSyntax statement && statement.Expression != null && statement.Expression.IsKind(SyntaxKind.NullLiteralExpression))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetLocation()));
                    }
                }
            }), SyntaxKind.MethodDeclaration);
        }
    }
}