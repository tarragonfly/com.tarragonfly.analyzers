using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8013: Where right after OrderBy, as in "enemies.OrderBy(e => e.Distance).Where(e =>
    // e.IsAlive)".
    //
    // The sort runs over every item, including the ones Where then throws away; filtering first sorts
    // fewer items.
    //
    // OrderBy and OrderByDescending count when Where follows directly, also through parentheses, "!"
    // or ?.; a ThenBy, Select or cast in between, query syntax, and the Where overload whose
    // condition takes the index, which depends on the sorted order, aren't checked. Reported on the
    // Where name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OrderByBeforeWhereAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8013",
            "OrderBy before Where",
            "Call Where before {0} so the sort only handles the items that are kept",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                IMethodSymbol where = LinqCalls.Method(context.SemanticModel, invocation, "Where", context.CancellationToken);
                if (where == null || !(where.Parameters[0].Type is INamedTypeSymbol condition) || condition.TypeArguments.Length != 2) return;
                ExpressionSyntax receiver = LinqCalls.Receiver(invocation);
                while (receiver is ParenthesizedExpressionSyntax || receiver.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    receiver = receiver is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : ((PostfixUnaryExpressionSyntax)receiver).Operand;
                }
                if (!(receiver is InvocationExpressionSyntax sort)) return;
                IMethodSymbol order = LinqCalls.Method(context.SemanticModel, sort, context.CancellationToken);
                if (order == null || (order.Name != "OrderBy" && order.Name != "OrderByDescending")) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), order.Name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}