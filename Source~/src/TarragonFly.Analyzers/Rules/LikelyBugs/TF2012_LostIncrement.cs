using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2012: a postfix ++ or -- on a local or parameter whose new value is thrown away, as in "count =
    // count++;" or "return combo++;".
    //
    // The expression yields the old value, so the variable ends up unchanged, or is never read again.
    //
    // Fields aren't checked, since "return field++;" does change the field. Reported on the ++ or --
    // expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LostIncrementAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2012",
            "Increment whose result is lost",
            "The new value of '{0}' is never used; write '{0} + 1' or move the increment to its own statement",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                PostfixUnaryExpressionSyntax postfix = (PostfixUnaryExpressionSyntax)context.Node;
                ISymbol symbol = context.SemanticModel.GetSymbolInfo(postfix.Operand, context.CancellationToken).Symbol;
                if (!(symbol is ILocalSymbol) && !(symbol is IParameterSymbol)) return;

                SyntaxNode node = postfix;
                SyntaxNode parent = postfix.Parent;
                while (parent is ParenthesizedExpressionSyntax)
                {
                    node = parent;
                    parent = parent.Parent;
                }

                bool lost = parent is ReturnStatementSyntax
                    || (parent is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                        && assignment.Right == node && SyntaxFactory.AreEquivalent(assignment.Left, postfix.Operand));
                if (lost) context.ReportDiagnostic(Diagnostic.Create(Rule, postfix.GetLocation(), postfix.Operand.ToString()));
            }), SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression);
        }
    }
}