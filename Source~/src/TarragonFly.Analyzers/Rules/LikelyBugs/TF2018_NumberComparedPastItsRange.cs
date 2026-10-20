using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2018: a float or integer compared with a float or double constant outside its range, such as
    // "distance > double.MaxValue" for a float or "ticks > 1e19" for a long, which is always false.
    //
    // Usually float.MaxValue or float.PositiveInfinity was meant, or the constant has a typo.
    //
    // Infinities, NaN and nullable values aren't checked. Integer constants that can never match, such
    // as "aByte > 300", are left to the C# compiler, which warns about them (CS0652). Reported on the
    // whole comparison. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NumberComparedPastItsRangeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2018",
            "Comparison that is always true or false",
            "{0}",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                ITypeSymbol type = TypeComparedPastItsRange(context.SemanticModel, comparison.Left, comparison.Right, context.CancellationToken)
                    ?? TypeComparedPastItsRange(context.SemanticModel, comparison.Right, comparison.Left, context.CancellationToken);
                if (type == null) return;
                string message = type.SpecialType == SpecialType.System_Single
                    ? "A float never reaches this value, so the comparison always gives the same result; was float.MaxValue meant?"
                    : $"A {type.ToDisplayString()} never reaches this value, so the comparison always gives the same result";
                context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation(), message));
            }), SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        // The type of the value when the float or double constant on the other side is outside its range.
        private static ITypeSymbol TypeComparedPastItsRange(SemanticModel model, ExpressionSyntax value, ExpressionSyntax constant, CancellationToken ct)
        {
            ITypeSymbol type = model.GetTypeInfo(value, ct).Type;
            if (type == null || !TryGetRange(type.SpecialType, out double min, out double max) || model.GetConstantValue(value, ct).HasValue) return null;
            Optional<object> folded = model.GetConstantValue(constant, ct);
            double number;
            switch (folded.HasValue ? folded.Value : null)
            {
                case double exact: number = exact; break;
                case float single: number = single; break;
                default: return null;
            }
            if (double.IsInfinity(number) || double.IsNaN(number)) return null;
            return number > max || number < min ? type : null;
        }

        private static bool TryGetRange(SpecialType type, out double min, out double max)
        {
            switch (type)
            {
                case SpecialType.System_Single: min = float.MinValue; max = float.MaxValue; return true;
                case SpecialType.System_SByte: min = sbyte.MinValue; max = sbyte.MaxValue; return true;
                case SpecialType.System_Byte: min = byte.MinValue; max = byte.MaxValue; return true;
                case SpecialType.System_Int16: min = short.MinValue; max = short.MaxValue; return true;
                case SpecialType.System_UInt16: min = ushort.MinValue; max = ushort.MaxValue; return true;
                case SpecialType.System_Char: min = char.MinValue; max = char.MaxValue; return true;
                case SpecialType.System_Int32: min = int.MinValue; max = int.MaxValue; return true;
                case SpecialType.System_UInt32: min = uint.MinValue; max = uint.MaxValue; return true;
                case SpecialType.System_Int64: min = long.MinValue; max = long.MaxValue; return true;
                case SpecialType.System_UInt64: min = ulong.MinValue; max = ulong.MaxValue; return true;
                default: min = max = 0; return false;
            }
        }
    }
}