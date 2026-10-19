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
    // OrderBy and OrderByDescending count when Where follows directly; a ThenBy or Select in between,
    // and query syntax, aren't checked. Reported on the Where name.
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
                if (LinqCalls.Method(context.SemanticModel, invocation, "Where", context.CancellationToken) == null) return;
                if (!(LinqCalls.Receiver(invocation) is InvocationExpressionSyntax sort)) return;
                IMethodSymbol order = LinqCalls.Method(context.SemanticModel, sort, context.CancellationToken);
                if (order == null || (order.Name != "OrderBy" && order.Name != "OrderByDescending")) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), order.Name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}