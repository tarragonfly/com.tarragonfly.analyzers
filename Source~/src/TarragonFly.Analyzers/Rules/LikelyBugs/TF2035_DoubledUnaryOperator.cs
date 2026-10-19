using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2035: "!!" or "~~" written directly in a row, which cancel out.
    //
    // "!!isAlive" is just isAlive; either one operator is a typo or the expression wasn't finished.
    //
    // Only ! and ~ without parentheses between them count, so "!(!x)" and "- -x" are left alone. A
    // longer run like "!!!x" is reported once, covering all of its operators.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DoubledUnaryOperatorAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2035",
            "Doubled ! or ~ operator",
            "Repeated '{0}' operators cancel each other out; remove the extra ones",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                PrefixUnaryExpressionSyntax outer = (PrefixUnaryExpressionSyntax)context.Node;
                if (outer.Parent.IsKind(outer.Kind())) return;

                PrefixUnaryExpressionSyntax last = outer;
                while (last.Operand is PrefixUnaryExpressionSyntax inner && inner.IsKind(outer.Kind())) last = inner;
                if (last == outer) return;

                TextSpan operators = TextSpan.FromBounds(outer.OperatorToken.SpanStart, last.OperatorToken.Span.End);
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(outer.SyntaxTree, operators), outer.OperatorToken.Text));
            }), SyntaxKind.LogicalNotExpression, SyntaxKind.BitwiseNotExpression);
        }
    }
}