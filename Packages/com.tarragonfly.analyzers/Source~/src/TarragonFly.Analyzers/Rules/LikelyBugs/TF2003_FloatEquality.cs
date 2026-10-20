using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2003: float or double values compared for exact equality.
    //
    // Rounding makes "speed == targetSpeed" or "transform.position.y == 2.5f" false when the values
    // only look equal; use Mathf.Approximately or a tolerance instead.
    //
    // Checked: == and != with a float or double (nullable included) on either side, the disguised forms
    // "a <= b && a >= b" and "a < b || a > b" on the same two values, and float.Equals or double.Equals
    // calls. Comparing with a constant 0 is fine, since 0 is exact, and so is comparing a nullable
    // with null, or Equals with NaN, which unlike == does find NaN. Equals(object) isn't checked.
    // Reported on the operator, the whole disguised expression, or the Equals name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FloatEqualityAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2003",
            "Floating-point values compared exactly",
            "Floating-point values rarely compare exactly equal; use Mathf.Approximately or compare with a tolerance",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                if (!IsFloatComparison(context.SemanticModel, comparison)) return;
                if (IsZeroOrNull(context.SemanticModel, comparison.Left) || IsZeroOrNull(context.SemanticModel, comparison.Right)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.OperatorToken.GetLocation()));
            }), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax combined = (BinaryExpressionSyntax)context.Node;
                if (IsDisguisedComparison(context.SemanticModel, combined)) context.ReportDiagnostic(Diagnostic.Create(Rule, combined.GetLocation()));
            }), SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || access.Name.Identifier.ValueText != "Equals") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || method.IsStatic) return;
                if (!IsFloatingPoint(method.ContainingType) || method.Parameters.Length != 1 || !IsFloatingPoint(method.Parameters[0].Type)) return;
                if (IsZeroOrNaN(context.SemanticModel, access.Expression) || IsZeroOrNaN(context.SemanticModel, invocation.ArgumentList.Arguments[0].Expression)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation()));
            }), SyntaxKind.InvocationExpression);
        }

        // "a <= b && a >= b" means a == b; "a < b || a > b" means a != b.
        private static bool IsDisguisedComparison(SemanticModel model, BinaryExpressionSyntax combined)
        {
            if (!(Unwrap(combined.Left) is BinaryExpressionSyntax first) || !(Unwrap(combined.Right) is BinaryExpressionSyntax second)) return false;
            bool and = combined.IsKind(SyntaxKind.LogicalAndExpression);
            SyntaxKind lower = and ? SyntaxKind.LessThanOrEqualExpression : SyntaxKind.LessThanExpression;
            SyntaxKind upper = and ? SyntaxKind.GreaterThanOrEqualExpression : SyntaxKind.GreaterThanExpression;
            bool kinds = (first.IsKind(lower) && second.IsKind(upper)) || (first.IsKind(upper) && second.IsKind(lower));
            if (!kinds || !IsFloatComparison(model, first)) return false;
            return SyntaxFactory.AreEquivalent(first.Left, second.Left) && SyntaxFactory.AreEquivalent(first.Right, second.Right);
        }

        private static bool IsFloatComparison(SemanticModel model, BinaryExpressionSyntax comparison)
        {
            return IsFloatingPoint(model.GetTypeInfo(comparison.Left).Type) || IsFloatingPoint(model.GetTypeInfo(comparison.Right).Type);
        }

        private static bool IsFloatingPoint(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T) type = named.TypeArguments[0];
            return type != null && (type.SpecialType == SpecialType.System_Single || type.SpecialType == SpecialType.System_Double);
        }

        private static bool IsZeroOrNull(SemanticModel model, ExpressionSyntax expression)
        {
            Optional<object> value = model.GetConstantValue(expression);
            return value.HasValue && (value.Value == null || ToDouble(value.Value) == 0d);
        }

        private static bool IsZeroOrNaN(SemanticModel model, ExpressionSyntax expression)
        {
            Optional<object> value = model.GetConstantValue(expression);
            if (!value.HasValue || value.Value == null) return false;
            double number = ToDouble(value.Value);
            return number == 0d || double.IsNaN(number);
        }

        // Convert.ToDouble throws on a char, which can stand on the other side of a float comparison.
        private static double ToDouble(object constant)
        {
            return constant is char character ? character : System.Convert.ToDouble(constant, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
            return expression;
        }
    }
}