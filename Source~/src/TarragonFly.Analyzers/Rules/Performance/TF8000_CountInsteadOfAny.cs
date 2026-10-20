using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8000: Count() compared with 0 or 1 to test for any items, as in "enemies.Count() > 0" or
    // "pickups.Count(p => p.IsRare) == 0".
    //
    // Count() goes through every item, while Any() stops at the first one it finds.
    //
    // Checked: > 0, != 0 and >= 1 (meaning Any), == 0, <= 0 and < 1 (meaning !Any), either way round,
    // with or without a condition, also through ?. as in "enemies?.Count() > 0". The Count property
    // and other comparisons are fine. Reported on the Count name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CountInsteadOfAnyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8000",
            "Count() used to test for any items",
            "Use {0} instead; Count() goes through every item while Any() stops at the first",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                SyntaxKind kind = comparison.Kind();
                InvocationExpressionSyntax count = CountCall(context, comparison.Left);
                ExpressionSyntax other = comparison.Right;
                if (count == null)
                {
                    count = CountCall(context, comparison.Right);
                    other = comparison.Left;
                    if (count == null) return;
                    kind = Mirrored(kind);
                }

                Optional<object> value = context.SemanticModel.GetConstantValue(other, context.CancellationToken);
                if (!value.HasValue || !(value.Value is int constant)) return;
                bool? any = TestsForAny(kind, constant);
                if (any.HasValue) context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(count), any.Value ? "Any()" : "!Any()"));
            }), SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.LessThanExpression,
                SyntaxKind.LessThanOrEqualExpression, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        // The LINQ Count() call the operand is, as in "items.Count()" or "items?.Count()", or null.
        private static InvocationExpressionSyntax CountCall(SyntaxNodeAnalysisContext context, ExpressionSyntax operand)
        {
            InvocationExpressionSyntax invocation = operand is ConditionalAccessExpressionSyntax conditional
                ? conditional.WhenNotNull as InvocationExpressionSyntax
                : operand as InvocationExpressionSyntax;
            return invocation != null && LinqCalls.Method(context.SemanticModel, invocation, "Count", context.CancellationToken) != null ? invocation : null;
        }

        // True for "Count() <kind> constant" meaning Any(), false for !Any(), null for anything else.
        private static bool? TestsForAny(SyntaxKind kind, int constant)
        {
            switch (kind)
            {
                case SyntaxKind.GreaterThanExpression when constant == 0:
                case SyntaxKind.NotEqualsExpression when constant == 0:
                case SyntaxKind.GreaterThanOrEqualExpression when constant == 1:
                    return true;
                case SyntaxKind.EqualsExpression when constant == 0:
                case SyntaxKind.LessThanOrEqualExpression when constant == 0:
                case SyntaxKind.LessThanExpression when constant == 1:
                    return false;
                default:
                    return null;
            }
        }

        private static SyntaxKind Mirrored(SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.GreaterThanExpression: return SyntaxKind.LessThanExpression;
                case SyntaxKind.GreaterThanOrEqualExpression: return SyntaxKind.LessThanOrEqualExpression;
                case SyntaxKind.LessThanExpression: return SyntaxKind.GreaterThanExpression;
                case SyntaxKind.LessThanOrEqualExpression: return SyntaxKind.GreaterThanOrEqualExpression;
                default: return kind;
            }
        }
    }
}