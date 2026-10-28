using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2006: the same expression on both sides of an operator, as in "a.x == a.x", or compared with
    // itself through Equals.
    //
    // The result is fixed (always true, always 0...), so one side is almost always a typo for
    // another variable, such as "target.x == target.x" instead of "target.x == player.x".
    //
    // Checked operators: == != && || - / % & | ^ < <= > >=, plus x.Equals(x) and Equals(x, x). + * <<
    // and >> are fine ("a + a" is just doubling), as is "f != f", the NaN check. Method calls count as
    // the same when written the same. Reported on the right side of an operator, and on the object or
    // first argument of an Equals call. Game code only.
    //
    // It reports "f != f" and "f == f" on floats and doubles too, which are the standard NaN check;
    // this rule doesn't.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IdenticalOperandsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2006",
            "Identical expressions on both sides",
            "Both sides of this operator are the same expression; one of them is probably meant to be something else",
            "LikelyBugs",
            CodeKind.Game);

        private static readonly SyntaxKind[] CheckedKinds =
        {
            SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression, SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
            SyntaxKind.SubtractExpression, SyntaxKind.DivideExpression, SyntaxKind.ModuloExpression, SyntaxKind.BitwiseAndExpression,
            SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression, SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanExpression, SyntaxKind.GreaterThanOrEqualExpression
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax binary = (BinaryExpressionSyntax)context.Node;
                if (!SyntaxFactory.AreEquivalent(binary.Left, binary.Right)) return;
                if ((binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)) && IsFloatingPoint(context, binary.Left)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, binary.Right.GetLocation()));
            }), CheckedKinds);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
                ExpressionSyntax target = invocation.Expression;
                string name = target is MemberAccessExpressionSyntax member ? member.Name.Identifier.ValueText : (target as IdentifierNameSyntax)?.Identifier.ValueText;
                if (name != "Equals") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;

                // x.Equals(x) is reported on x, Equals(x, x) and object.Equals(x, x) on the first x.
                if (!method.IsStatic && arguments.Count == 1 && target is MemberAccessExpressionSyntax access && SyntaxFactory.AreEquivalent(access.Expression, arguments[0].Expression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, access.Expression.GetLocation()));
                }
                else if (method.IsStatic && arguments.Count == 2 && SyntaxFactory.AreEquivalent(arguments[0].Expression, arguments[1].Expression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, arguments[0].GetLocation()));
                }
            }), SyntaxKind.InvocationExpression);
        }

        private static bool IsFloatingPoint(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            ITypeSymbol type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
            return type != null && (type.SpecialType == SpecialType.System_Single || type.SpecialType == SpecialType.System_Double);
        }
    }
}