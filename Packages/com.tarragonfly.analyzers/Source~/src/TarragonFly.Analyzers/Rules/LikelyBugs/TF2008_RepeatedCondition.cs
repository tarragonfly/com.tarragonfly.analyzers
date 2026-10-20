using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2008: an else-if condition that repeats an earlier condition of the same chain.
    //
    // The repeated branch can never run, since the earlier one always catches the case first; usually
    // it was copied and not edited, as in "else if (state == State.Idle)" twice.
    //
    // Conditions count as repeated when written the same, even if they call methods. Separate if
    // statements, ?: chains and switch case guards aren't checked. Reported on the repeated condition.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RepeatedConditionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2008",
            "Repeated else-if condition",
            "This condition repeats the one on line {0}, so this branch never runs",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                IfStatementSyntax first = (IfStatementSyntax)context.Node;
                if (!Branches.IsChainStart(first)) return;

                List<ExpressionSyntax> earlier = new List<ExpressionSyntax>();
                for (IfStatementSyntax current = first; current != null; current = current.Else?.Statement as IfStatementSyntax)
                {
                    foreach (ExpressionSyntax condition in earlier)
                    {
                        if (!SyntaxFactory.AreEquivalent(condition, current.Condition)) continue;
                        int line = condition.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, current.Condition.GetLocation(), line));
                        break;
                    }
                    earlier.Add(current.Condition);
                }
            }), SyntaxKind.IfStatement);
        }
    }
}