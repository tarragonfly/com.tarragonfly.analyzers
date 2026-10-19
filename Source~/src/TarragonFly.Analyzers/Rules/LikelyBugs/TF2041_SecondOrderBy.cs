using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2041: OrderBy called right after another sort, as in "enemies.OrderBy(e => e.Team).OrderBy(e =>
    // e.Distance)".
    //
    // The second OrderBy re-sorts everything and throws the first order away; ThenBy keeps it as the
    // tie-breaker.
    //
    // OrderBy and OrderByDescending count after OrderBy, OrderByDescending, ThenBy or ThenByDescending,
    // with nothing in between; a Where or Select in between starts over. In query syntax, an orderby
    // clause right after another counts. Reported on the second OrderBy, or orderby keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SecondOrderByAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2041",
            "Second OrderBy discards the first sort",
            "This sort throws away the one before it; use ThenBy or ThenByDescending to keep it",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || !IsOrderBy(access.Name.Identifier.ValueText)) return;
                if (!(access.Expression is InvocationExpressionSyntax previous) || !(previous.Expression is MemberAccessExpressionSyntax previousAccess)) return;
                string previousName = previousAccess.Name.Identifier.ValueText;
                if (!IsOrderBy(previousName) && previousName != "ThenBy" && previousName != "ThenByDescending") return;
                if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation()));
                }
            }), SyntaxKind.InvocationExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                QueryBodySyntax body = (QueryBodySyntax)context.Node;
                for (int i = 1; i < body.Clauses.Count; i++)
                {
                    if (body.Clauses[i] is OrderByClauseSyntax second && body.Clauses[i - 1] is OrderByClauseSyntax)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, second.OrderByKeyword.GetLocation()));
                    }
                }
            }), SyntaxKind.QueryBody);
        }

        private static bool IsOrderBy(string name)
        {
            return name == "OrderBy" || name == "OrderByDescending";
        }
    }
}