using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2034: "=+", "=-" or "=!" where "+=", "-=" or "!=" was almost certainly meant, as in "score =+
    // points;", which sets the score instead of adding to it.
    //
    // Reported when a unary +, - or ! touches the "=" of an assignment or initializer. For - it also
    // needs a space after it ("x =- y"), since "x =-1" is a common way to write a negative value. "=~"
    // isn't checked. Reported on the unary operator.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReversedCompoundOperatorAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2034",
            "Reversed compound operator",
            "'={0}' looks like a reversed '{0}='; write '{0}=' or put a space after '='",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxToken equals = context.Node is AssignmentExpressionSyntax assignment
                    ? assignment.OperatorToken
                    : ((EqualsValueClauseSyntax)context.Node).EqualsToken;
                SyntaxToken unary = equals.GetNextToken();
                if (unary.SpanStart != equals.Span.End || !(unary.Parent is PrefixUnaryExpressionSyntax prefix) || prefix.OperatorToken != unary) return;

                bool reversed = unary.IsKind(SyntaxKind.PlusToken)
                    || unary.IsKind(SyntaxKind.ExclamationToken)
                    || (unary.IsKind(SyntaxKind.MinusToken) && unary.GetNextToken().SpanStart > unary.Span.End);
                if (reversed) context.ReportDiagnostic(Diagnostic.Create(Rule, unary.GetLocation(), unary.Text));
            }), SyntaxKind.SimpleAssignmentExpression, SyntaxKind.EqualsValueClause);
        }
    }
}