using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2032: a comparison with float.NaN or double.NaN, as in "speed == float.NaN".
    //
    // NaN compares false with everything, itself included (and != is always true), so the check never
    // finds the NaN; use float.IsNaN(speed).
    //
    // All six comparison operators count. NaN.Equals(NaN) is true and fine. Reported on the whole
    // comparison.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NaNComparisonAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2032",
            "Comparison with NaN",
            "Comparisons with NaN are never true (!= is always true); use float.IsNaN or double.IsNaN",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                if (IsNaN(context.SemanticModel, comparison.Left) || IsNaN(context.SemanticModel, comparison.Right))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation()));
                }
            }), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression, SyntaxKind.LessThanExpression,
                SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanOrEqualExpression);
        }

        private static bool IsNaN(SemanticModel model, ExpressionSyntax expression)
        {
            if (!(model.GetSymbolInfo(expression).Symbol is IFieldSymbol field) || field.Name != "NaN") return false;
            return field.ContainingType.SpecialType == SpecialType.System_Single || field.ContainingType.SpecialType == SpecialType.System_Double;
        }
    }
}