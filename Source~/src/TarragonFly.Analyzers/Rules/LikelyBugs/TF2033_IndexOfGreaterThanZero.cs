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
    // IndexOf, IndexOfAny, LastIndexOf and LastIndexOfAny count, on strings, arrays and lists, in
    // either order ("0 < IndexOf(...)"). Reported on the "> 0" part.
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
            return expression is InvocationExpressionSyntax invocation
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                && Methods.Contains(method.Name)
                && method.ReturnType.SpecialType == SpecialType.System_Int32;
        }

        private static bool IsZero(SemanticModel model, ExpressionSyntax expression)
        {
            Optional<object> value = model.GetConstantValue(expression);
            return value.HasValue && value.Value is int number && number == 0;
        }
    }
}