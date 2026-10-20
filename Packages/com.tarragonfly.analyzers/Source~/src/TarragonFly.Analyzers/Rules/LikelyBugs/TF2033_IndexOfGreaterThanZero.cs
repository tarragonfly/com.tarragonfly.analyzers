using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2033: "IndexOf(...) > 0" used to test whether something was found.
    //
    // A match at the very start is index 0, so "name.IndexOf("Boss") > 0" misses "BossGoblin"; compare
    // with >= 0 or != -1, or use Contains.
    //
    // IndexOf, IndexOfAny, LastIndexOf and LastIndexOfAny count, on strings, arrays, lists and any
    // other type, in either order ("0 < IndexOf(...)"), also inside parentheses or checked(...). Any
    // constant zero counts, such as 0L, 0f, '\0' or a named constant. Reported on the "> 0" part.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IndexOfGreaterThanZeroAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2033",
            "IndexOf(...) > 0 misses index 0",
            "A match at index 0 fails this check; compare with >= 0 or use Contains",
            "LikelyBugs",
            CodeKind.All);

        private static readonly HashSet<string> Methods = new HashSet<string> { "IndexOf", "IndexOfAny", "LastIndexOf", "LastIndexOfAny" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                if (comparison.IsKind(SyntaxKind.GreaterThanExpression) && IsIndexOf(context.SemanticModel, comparison.Left) && IsZero(context.SemanticModel, comparison.Right))
                {
                    Report(context, TextSpan.FromBounds(comparison.OperatorToken.SpanStart, comparison.Right.Span.End));
                }
                else if (comparison.IsKind(SyntaxKind.LessThanExpression) && IsZero(context.SemanticModel, comparison.Left) && IsIndexOf(context.SemanticModel, comparison.Right))
                {
                    Report(context, TextSpan.FromBounds(comparison.Left.SpanStart, comparison.OperatorToken.Span.End));
                }
            }), SyntaxKind.GreaterThanExpression, SyntaxKind.LessThanExpression);
        }

        private static void Report(SyntaxNodeAnalysisContext context, TextSpan span)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Node.SyntaxTree, span)));
        }

        private static bool IsIndexOf(SemanticModel model, ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
                else if (expression is CheckedExpressionSyntax checkedExpression) expression = checkedExpression.Expression;
                else break;
            }
            return expression is InvocationExpressionSyntax invocation
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && Methods.Contains(method.Name)
                && method.ReturnType.SpecialType == SpecialType.System_Int32;
        }

        private static bool IsZero(SemanticModel model, ExpressionSyntax expression)
        {
            Optional<object> value = model.GetConstantValue(expression);
            if (!value.HasValue) return false;
            switch (value.Value)
            {
                case int number: return number == 0;
                case long number: return number == 0;
                case uint number: return number == 0;
                case ulong number: return number == 0;
                case short number: return number == 0;
                case ushort number: return number == 0;
                case byte number: return number == 0;
                case sbyte number: return number == 0;
                case char character: return character == 0;
                case float number: return number == 0;
                case double number: return number == 0;
                case decimal number: return number == 0;
                default: return false;
            }
        }
    }
}