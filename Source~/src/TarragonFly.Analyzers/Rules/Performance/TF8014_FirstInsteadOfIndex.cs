using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8014: First(), Last() or ElementAt(i) on a list or an array, as in "waypoints.Last()".
    //
    // The collection has an indexer, so "waypoints[waypoints.Count - 1]" gets the item directly
    // instead of going through LINQ.
    //
    // Arrays, IList<T> and IReadOnlyList<T> (List<T> included) count; strings and plain sequences
    // don't, and neither do the overloads taking a condition. Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FirstInsteadOfIndexAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8014",
            "First, Last or ElementAt on an indexable list",
            "Use the indexer instead of {0}(); this collection can reach the item directly",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                IMethodSymbol method = LinqCalls.Method(context.SemanticModel, invocation, context.CancellationToken);
                if (method == null) return;
                int expected = method.Name == "ElementAt" ? 1 : method.Name == "First" || method.Name == "Last" ? 0 : -1;
                if (expected < 0 || invocation.ArgumentList.Arguments.Count != expected) return;
                if (!IsIndexable(LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), method.Name));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool IsIndexable(ITypeSymbol type)
        {
            if (type == null || type.SpecialType == SpecialType.System_String) return false;
            if (type is IArrayTypeSymbol) return true;
            return SymbolNames.Implements(type, candidate =>
                SymbolNames.IsNamed(candidate.OriginalDefinition, "System.Collections.Generic.IList`1")
                || SymbolNames.IsNamed(candidate.OriginalDefinition, "System.Collections.Generic.IReadOnlyList`1"));
        }
    }
}