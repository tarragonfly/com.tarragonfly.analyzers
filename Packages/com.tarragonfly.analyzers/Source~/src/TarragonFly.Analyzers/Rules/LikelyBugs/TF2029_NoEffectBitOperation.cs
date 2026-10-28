using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2029: a bit operation that leaves the value unchanged: "x | 0", "x ^ 0", "x & -1", "flag |
    // false", and the compound forms "x |= 0", "x ^= 0", "x &= -1".
    //
    // Usually a different mask or layer bit was meant, as in "mask | 0" instead of "mask | 1 << layer".
    //
    // Only literal 0, -1 and false count; a named constant that happens to be 0 is fine. Reported on
    // the operator and the literal, or on the whole compound assignment.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NoEffectBitOperationAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2029",
            "Bit operation with no effect",
            "This bit operation leaves the value unchanged; was another mask meant?",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax binary = (BinaryExpressionSyntax)context.Node;
                SyntaxKind kind = binary.Kind();
                if (IsIdentity(kind, binary.Right))
                {
                    TextSpan span = TextSpan.FromBounds(binary.OperatorToken.SpanStart, binary.Right.Span.End);
                    context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(binary.SyntaxTree, span)));
                }
                else if (IsIdentity(kind, binary.Left))
                {
                    TextSpan span = TextSpan.FromBounds(binary.Left.SpanStart, binary.OperatorToken.Span.End);
                    context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(binary.SyntaxTree, span)));
                }
            }), SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression, SyntaxKind.BitwiseAndExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                SyntaxKind kind = assignment.IsKind(SyntaxKind.AndAssignmentExpression) ? SyntaxKind.BitwiseAndExpression
                    : assignment.IsKind(SyntaxKind.OrAssignmentExpression) ? SyntaxKind.BitwiseOrExpression : SyntaxKind.ExclusiveOrExpression;
                if (IsIdentity(kind, assignment.Right)) context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation()));
            }), SyntaxKind.OrAssignmentExpression, SyntaxKind.ExclusiveOrAssignmentExpression, SyntaxKind.AndAssignmentExpression);
        }

        // 0 or false for | and ^, -1 for &.
        private static bool IsIdentity(SyntaxKind operation, ExpressionSyntax operand)
        {
            if (operation == SyntaxKind.BitwiseAndExpression)
            {
                return operand is PrefixUnaryExpressionSyntax negative && negative.IsKind(SyntaxKind.UnaryMinusExpression)
                    && negative.Operand is LiteralExpressionSyntax one && one.IsKind(SyntaxKind.NumericLiteralExpression) && IsValue(one, 1);
            }
            if (operand.IsKind(SyntaxKind.FalseLiteralExpression)) return true;
            return operand is LiteralExpressionSyntax zero && zero.IsKind(SyntaxKind.NumericLiteralExpression) && IsValue(zero, 0);
        }

        private static bool IsValue(LiteralExpressionSyntax literal, long expected)
        {
            object value = literal.Token.Value;
            if (value is float || value is double || value is decimal) return false;
            return value != null && Convert.ToDecimal(value, CultureInfo.InvariantCulture) == expected;
        }
    }
}
