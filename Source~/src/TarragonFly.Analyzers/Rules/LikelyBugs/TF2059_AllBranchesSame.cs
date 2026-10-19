using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2059: an if/else chain, switch, ?: or switch expression whose branches all do the same thing,
    // so the condition has no effect.
    //
    // Usually one branch was meant to differ: "isCritical ? baseDamage : baseDamage".
    //
    // An if chain needs a final else and a switch statement a default section, or some inputs would run
    // no branch at all; a switch expression needs a final discard arm (_). Comments and formatting are
    // ignored. Reported on the if or switch keyword, or for ?: from the condition to the "?".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AllBranchesSameAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2059",
            "Every branch has the same code",
            "Every branch here does the same thing, so the condition has no effect",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeIf), SyntaxKind.IfStatement);
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeSwitch), SyntaxKind.SwitchStatement);
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeConditional), SyntaxKind.ConditionalExpression);
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeSwitchExpression), SyntaxKind.SwitchExpression);
        }

        private static void AnalyzeIf(SyntaxNodeAnalysisContext context)
        {
            IfStatementSyntax first = (IfStatementSyntax)context.Node;
            if (!Branches.IsChainStart(first)) return;
            List<StatementSyntax> bodies = Branches.OfIfChain(first, out bool endsWithElse);
            if (!endsWithElse) return;

            IReadOnlyList<StatementSyntax> firstCode = Branches.Statements(bodies[0]);
            for (int i = 1; i < bodies.Count; i++)
            {
                if (!Branches.HaveSameCode(firstCode, Branches.Statements(bodies[i]))) return;
            }
            context.ReportDiagnostic(Diagnostic.Create(Rule, first.IfKeyword.GetLocation()));
        }

        private static void AnalyzeSwitch(SyntaxNodeAnalysisContext context)
        {
            SwitchStatementSyntax switchStatement = (SwitchStatementSyntax)context.Node;
            SyntaxList<SwitchSectionSyntax> sections = switchStatement.Sections;
            if (sections.Count < 2) return;

            bool hasDefault = false;
            foreach (SwitchSectionSyntax section in sections)
            {
                if (!Branches.HaveSameCode(sections[0].Statements, section.Statements)) return;
                hasDefault |= Branches.HasDefaultLabel(section);
            }
            if (hasDefault) context.ReportDiagnostic(Diagnostic.Create(Rule, switchStatement.SwitchKeyword.GetLocation()));
        }

        private static void AnalyzeConditional(SyntaxNodeAnalysisContext context)
        {
            ConditionalExpressionSyntax conditional = (ConditionalExpressionSyntax)context.Node;
            if (!SyntaxFactory.AreEquivalent(conditional.WhenTrue, conditional.WhenFalse)) return;
            TextSpan span = TextSpan.FromBounds(conditional.Condition.SpanStart, conditional.QuestionToken.Span.End);
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(conditional.SyntaxTree, span)));
        }

        private static void AnalyzeSwitchExpression(SyntaxNodeAnalysisContext context)
        {
            SwitchExpressionSyntax switchExpression = (SwitchExpressionSyntax)context.Node;
            SeparatedSyntaxList<SwitchExpressionArmSyntax> arms = switchExpression.Arms;
            if (arms.Count < 2 || !(arms[arms.Count - 1].Pattern is DiscardPatternSyntax)) return;
            foreach (SwitchExpressionArmSyntax arm in arms)
            {
                if (!SyntaxFactory.AreEquivalent(arms[0].Expression, arm.Expression)) return;
            }
            context.ReportDiagnostic(Diagnostic.Create(Rule, switchExpression.SwitchKeyword.GetLocation()));
        }
    }
}