using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2017: "x % n == k" with a signed x and a non-zero k, such as "turn % 2 == 1".
    //
    // C# keeps the sign of x, so a negative x gives -1, not 1, and the check is false; compare with 0
    // ("x % 2 != 0") or use Math.Abs.
    //
    // Unsigned values and Length, LongLength and Count, which can't be negative, are fine. == and !=
    // count, in either order. Reported on the whole comparison.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OddCheckWithModulusAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2017",
            "Remainder compared with a non-zero value",
            "A negative value gives a negative remainder, so this check fails for it; compare the remainder with 0 instead",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                if (IsSignedRemainder(context.SemanticModel, comparison.Left) && IsNonZeroConstant(context.SemanticModel, comparison.Right)
                    || IsSignedRemainder(context.SemanticModel, comparison.Right) && IsNonZeroConstant(context.SemanticModel, comparison.Left))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation()));
                }
            }), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        private static bool IsSignedRemainder(SemanticModel model, ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
            if (!(expression is BinaryExpressionSyntax remainder) || !remainder.IsKind(SyntaxKind.ModuloExpression)) return false;
            if (remainder.Left is MemberAccessExpressionSyntax access && IsNeverNegative(access.Name.Identifier.ValueText)) return false;
            switch (model.GetTypeInfo(remainder.Left).Type?.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsNeverNegative(string member)
        {
            return member == "Length" || member == "LongLength" || member == "Count";
        }

        private static bool IsNonZeroConstant(SemanticModel model, ExpressionSyntax expression)
        {
            Optional<object> value = model.GetConstantValue(expression);
            if (!value.HasValue || value.Value == null) return false;
            // Convert.ToDouble throws on a char, which can stand on the other side of the remainder.
            return value.Value is char character ? character != 0 : Convert.ToDouble(value.Value, CultureInfo.InvariantCulture) != 0;
        }
    }
}
