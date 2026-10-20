using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2004: a variable, field, property or element assigned to itself, as in "health = health;".
    //
    // Nearly always a slip: in a constructor "health = health;" assigns the parameter to itself and
    // leaves the field at its default.
    //
    // Both sides must be written the same, so "this.health = health" isn't reported, and neither is
    // "new Save { Slot = Slot }", where the left side is the new object's member. Tuple assignments are
    // checked element by element. Reported on the left side.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SelfAssignmentAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2004",
            "Variable assigned to itself",
            "This assigns '{0}' to itself; was another variable or 'this.' meant?",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                if (assignment.Parent.IsKind(SyntaxKind.ObjectInitializerExpression) || assignment.Parent.IsKind(SyntaxKind.WithInitializerExpression)) return;
                if (assignment.Left is TupleExpressionSyntax left && assignment.Right is TupleExpressionSyntax right && left.Arguments.Count == right.Arguments.Count)
                {
                    for (int i = 0; i < left.Arguments.Count; i++) Check(context, left.Arguments[i].Expression, right.Arguments[i].Expression);
                    return;
                }
                Check(context, assignment.Left, assignment.Right);
            }), SyntaxKind.SimpleAssignmentExpression);
        }

        private static void Check(SyntaxNodeAnalysisContext context, ExpressionSyntax left, ExpressionSyntax right)
        {
            if (SyntaxFactory.AreEquivalent(left, right)) context.ReportDiagnostic(Diagnostic.Create(Rule, left.GetLocation(), left.ToString()));
        }
    }
}