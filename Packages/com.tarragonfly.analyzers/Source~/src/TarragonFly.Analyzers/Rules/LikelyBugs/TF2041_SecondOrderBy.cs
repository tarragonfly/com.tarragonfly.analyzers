using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2041: OrderBy called right after another sort, as in "enemies.OrderBy(e => e.Team).OrderBy(e =>
    // e.Distance)".
    //
    // The second OrderBy re-sorts everything and throws the first order away; ThenBy keeps it as the
    // tie-breaker.
    //
    // LINQ's OrderBy, OrderByDescending, Order and OrderDescending count when called on something
    // already sorted, an IOrderedEnumerable or IOrderedQueryable, however it got there: straight
    // after another sort, in parentheses, through ?., from a variable, or passed to the static
    // Enumerable.OrderBy form. A Where or Select in between starts over, and an OrderBy method of
    // another type isn't checked. In query syntax, an orderby clause right after another counts.
    // Reported on the second OrderBy, or orderby keyword.
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
                SimpleNameSyntax name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name
                    : invocation.Expression is MemberBindingExpressionSyntax binding ? binding.Name : null;
                if (name == null || !IsOrderBy(name.Identifier.ValueText)) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || !method.IsExtensionMethod) return;
                if (!SymbolNames.IsInNamespace((method.ReducedFrom ?? method).ContainingType, "System.Linq")) return;
                ExpressionSyntax sorted = method.ReducedFrom == null ? invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression : Receiver(invocation);
                if (sorted != null && IsSorted(context.SemanticModel.GetTypeInfo(sorted, context.CancellationToken).Type))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation()));
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
            return name == "OrderBy" || name == "OrderByDescending" || name == "Order" || name == "OrderDescending";
        }

        // "items" in items.OrderBy(...) and items?.OrderBy(...).
        private static ExpressionSyntax Receiver(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax access) return access.Expression;
            for (SyntaxNode node = invocation; node.Parent != null; node = node.Parent)
            {
                if (node.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == node) return conditional.Expression;
            }
            return null;
        }

        private static bool IsSorted(ITypeSymbol type)
        {
            if (type == null) return false;
            foreach (ITypeSymbol candidate in new[] { type }.Concat(type.AllInterfaces))
            {
                if (SymbolNames.IsNamed(candidate.OriginalDefinition, "System.Linq.IOrderedEnumerable`1") || SymbolNames.IsNamed(candidate.OriginalDefinition, "System.Linq.IOrderedQueryable`1")) return true;
            }
            return false;
        }
    }
}