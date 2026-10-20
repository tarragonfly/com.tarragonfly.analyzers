using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1014: an if/else-if branch or switch section with the same code as an earlier one in the same
    // chain or switch.
    //
    // Either the conditions should be combined, or one copy was meant to differ and was pasted
    // without being changed, which is a common source of "this button does the same as that one" bugs.
    //
    // Comments and formatting are ignored when comparing. If branches only take part when they have
    // braces, and switch sections only when their labels declare no pattern variable. An else block
    // holding nothing but an if continues the chain, like "else if". A branch with a single statement
    // on one line is skipped, since short identical branches are often deliberate; a final break,
    // return or throw doesn't count toward that. When there's no final else or default and every
    // branch up to the copy is the same, short branches are reported too, unless all they do is jump
    // (return, break, continue, goto or throw); with every branch the same, that's the case TF2059
    // leaves out. The later copy is reported: the whole block, or the whole switch section. Game code
    // only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DuplicateBranchAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1014",
            "Branches with identical code",
            "This branch has the same code as the one on line {0}; combine their conditions or fix the copy",
            "Readability",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeIf), SyntaxKind.IfStatement);
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeSwitch), SyntaxKind.SwitchStatement);
        }

        private static void AnalyzeIf(SyntaxNodeAnalysisContext context)
        {
            IfStatementSyntax first = (IfStatementSyntax)context.Node;
            if (!Branches.IsChainStart(first) || Branches.IsOnlyStatementOfElse(first)) return;

            List<StatementSyntax> bodies = Branches.OfIfChain(first, out bool endsWithElse, throughElseBlocks: true);
            List<IReadOnlyList<StatementSyntax>> code = new List<IReadOnlyList<StatementSyntax>>();
            foreach (StatementSyntax body in bodies) code.Add(body is BlockSyntax block ? (IReadOnlyList<StatementSyntax>)block.Statements : null);
            ReportCopies(context, code, i => bodies[i].Span, coversEveryCase: endsWithElse);
        }

        private static void AnalyzeSwitch(SyntaxNodeAnalysisContext context)
        {
            SwitchStatementSyntax switchStatement = (SwitchStatementSyntax)context.Node;
            List<IReadOnlyList<StatementSyntax>> code = new List<IReadOnlyList<StatementSyntax>>();
            bool hasDefault = false;
            foreach (SwitchSectionSyntax section in switchStatement.Sections)
            {
                code.Add(DeclaresPatternVariable(section) ? null : (IReadOnlyList<StatementSyntax>)section.Statements);
                hasDefault |= Branches.HasDefaultLabel(section);
            }
            ReportCopies(context, code, i => switchStatement.Sections[i].Span, coversEveryCase: hasDefault);
        }

        private static void ReportCopies(SyntaxNodeAnalysisContext context, List<IReadOnlyList<StatementSyntax>> code, Func<int, TextSpan> spanOf, bool coversEveryCase)
        {
            for (int later = 1; later < code.Count; later++)
            {
                if (code[later] == null) continue;
                bool reportShort = !coversEveryCase && SameAsEveryEarlier(code, later);
                if (reportShort ? OnlyJumps(code[later]) : IsShort(code[later])) continue;
                for (int earlier = 0; earlier < later; earlier++)
                {
                    if (code[earlier] == null || !Branches.HaveSameCode(code[earlier], code[later])) continue;
                    Location location = Location.Create(context.Node.SyntaxTree, spanOf(later));
                    int line = Location.Create(context.Node.SyntaxTree, spanOf(earlier)).GetLineSpan().StartLinePosition.Line + 1;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, line));
                    break;
                }
            }
        }

        // "case GoToBranch branch:" and "case NestBranch branch:" can hold the same text and still do
        // different things, since each branch variable has another type.
        private static bool DeclaresPatternVariable(SwitchSectionSyntax section)
        {
            foreach (SwitchLabelSyntax label in section.Labels)
            {
                if (label is CasePatternSwitchLabelSyntax patternLabel && patternLabel.Pattern.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>().Any()) return true;
            }
            return false;
        }

        private static bool SameAsEveryEarlier(List<IReadOnlyList<StatementSyntax>> code, int later)
        {
            for (int earlier = 0; earlier < later; earlier++)
            {
                if (code[earlier] == null || !Branches.HaveSameCode(code[earlier], code[later])) return false;
            }
            return true;
        }

        // One statement on one line, not counting a final break, return or throw.
        private static bool IsShort(IReadOnlyList<StatementSyntax> statements)
        {
            int count = CountWithoutFinalExit(statements);
            if (count != 1) return count == 0;
            FileLinePositionSpan lines = statements[0].GetLocation().GetLineSpan();
            return lines.StartLinePosition.Line == lines.EndLinePosition.Line;
        }

        private static bool OnlyJumps(IReadOnlyList<StatementSyntax> statements)
        {
            int count = CountWithoutFinalExit(statements);
            if (count != 1) return count == 0;
            StatementSyntax only = statements[0];
            return only is ReturnStatementSyntax || only is ContinueStatementSyntax || only is GotoStatementSyntax || only is ThrowStatementSyntax;
        }

        private static int CountWithoutFinalExit(IReadOnlyList<StatementSyntax> statements)
        {
            int count = statements.Count;
            if (count == 0) return 0;
            StatementSyntax last = statements[count - 1];
            return last is BreakStatementSyntax || last is ReturnStatementSyntax || last is ThrowStatementSyntax ? count - 1 : count;
        }
    }
}