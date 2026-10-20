using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2061: a count or length compared so the answer never changes, as in "enemies.Count >= 0" or
    // "path.Length < 0".
    //
    // Counts are never negative, so the check is always true or always false; the author usually meant
    // "> 0" or "== 0", and the empty case slips through.
    //
    // Checked: Count on collections, Length on strings and arrays, LongLength on arrays and
    // Enumerable.Count(), compared with a constant zero or below, either way round. A Count or Length
    // property on any other type isn't checked, since it may well go negative. Reported on the whole
    // comparison.
    //
    // Enumerable.LongCount() counts too.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CountComparisonAlwaysSameAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2061",
            "Count comparison that is always the same",
            "A count is never negative, so this is always {0}; did you mean '> 0' or '== 0'?",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                SyntaxKind kind = comparison.Kind();
                double? constant = Constant(context, comparison.Right);
                bool countOnLeft = constant.HasValue && IsCount(context, comparison.Left);
                if (!countOnLeft)
                {
                    constant = Constant(context, comparison.Left);
                    if (!constant.HasValue || !IsCount(context, comparison.Right)) return;
                    kind = Mirrored(kind);
                }

                bool? result = Result(kind, constant.Value);
                if (result.HasValue) context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation(), result.Value ? "true" : "false"));
            }), SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanExpression,
                SyntaxKind.LessThanOrEqualExpression, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        // The answer for "count <kind> constant" when it doesn't depend on the count, or null.
        private static bool? Result(SyntaxKind kind, double constant)
        {
            switch (kind)
            {
                case SyntaxKind.GreaterThanOrEqualExpression: return constant <= 0 ? true : (bool?)null;
                case SyntaxKind.LessThanExpression: return constant <= 0 ? false : (bool?)null;
                case SyntaxKind.GreaterThanExpression: return constant < 0 ? true : (bool?)null;
                case SyntaxKind.LessThanOrEqualExpression: return constant < 0 ? false : (bool?)null;
                case SyntaxKind.EqualsExpression: return constant < 0 ? false : (bool?)null;
                case SyntaxKind.NotEqualsExpression: return constant < 0 ? true : (bool?)null;
                default: return null;
            }
        }

        private static SyntaxKind Mirrored(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.GreaterThanOrEqualExpression: return SyntaxKind.LessThanOrEqualExpression;
                case SyntaxKind.GreaterThanExpression: return SyntaxKind.LessThanExpression;
                case SyntaxKind.LessThanExpression: return SyntaxKind.GreaterThanExpression;
                case SyntaxKind.LessThanOrEqualExpression: return SyntaxKind.GreaterThanOrEqualExpression;
                default: return kind;
            }
        }

        private static double? Constant(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            Optional<object> value = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
            if (!value.HasValue || value.Value == null || value.Value is string || value.Value is bool || value.Value is char) return null;
            return System.Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool IsCount(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            ISymbol symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
            if (symbol is IPropertySymbol property)
            {
                INamedTypeSymbol type = property.ContainingType;
                switch (property.Name)
                {
                    case "Count": return SymbolNames.Implements(type, IsCollectionInterface);
                    case "Length": return type.SpecialType == SpecialType.System_String || type.SpecialType == SpecialType.System_Array;
                    case "LongLength": return type.SpecialType == SpecialType.System_Array;
                    default: return false;
                }
            }
            return symbol is IMethodSymbol method && (method.Name == "Count" || method.Name == "LongCount")
                && SymbolNames.IsNamed(method.ContainingType, "System.Linq.Enumerable");
        }

        private static bool IsCollectionInterface(INamedTypeSymbol type)
        {
            return SymbolNames.IsNamed(type, "System.Collections.ICollection")
                || SymbolNames.IsNamed(type.OriginalDefinition, "System.Collections.Generic.ICollection`1")
                || SymbolNames.IsNamed(type.OriginalDefinition, "System.Collections.Generic.IReadOnlyCollection`1");
        }
    }
}