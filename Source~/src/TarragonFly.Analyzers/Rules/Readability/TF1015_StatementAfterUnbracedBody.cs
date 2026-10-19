using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1015: a statement placed as if it were part of the unbraced body of the if, else, for, foreach
    // or while above it, although it always runs.
    //
    //     if (isDead)
    //         PlayDeathSound();
    //         Respawn();        // runs every time
    //
    // The next statement is reported when it starts on the line where the body ends, or, for a body on
    // its own line, at or past the body's column, or, for a body on the same line as its keyword, past
    // the keyword's column, with tabs counted as 4 columns. Blank lines and comments in between don't
    // matter. For an else-if chain the last branch's body is the one compared. do loops aren't checked.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StatementAfterUnbracedBodyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1015",
            "Statement looks like part of an unbraced body",
            "This statement is lined up with the unbraced body above it but always runs; add braces or fix the indentation",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(Analyze),
                SyntaxKind.IfStatement, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement);
        }

        private static void Analyze(SyntaxNodeAnalysisContext context)
        {
            StatementSyntax statement = (StatementSyntax)context.Node;
            StatementSyntax next = NextStatement(statement);
            if (next == null) return;

            StatementSyntax body = LastBody(statement, out SyntaxToken keyword);
            if (body is BlockSyntax) return;

            FileLinePositionSpan bodyLines = body.GetLocation().GetLineSpan();
            int nextLine = next.GetLocation().GetLineSpan().StartLinePosition.Line;
            int keywordLine = keyword.GetLocation().GetLineSpan().StartLinePosition.Line;
            int nextColumn = VisualColumns.Of(next);

            bool looksInside;
            if (nextLine == bodyLines.EndLinePosition.Line) looksInside = true;
            else if (bodyLines.StartLinePosition.Line == keywordLine) looksInside = nextColumn > VisualColumns.Of(keyword);
            else looksInside = nextColumn >= VisualColumns.Of(body);

            if (looksInside) context.ReportDiagnostic(Diagnostic.Create(Rule, next.GetLocation()));
        }

        // The body that runs last in source order: an if's else branch, following else-if chains.
        private static StatementSyntax LastBody(StatementSyntax statement, out SyntaxToken keyword)
        {
            switch (statement)
            {
                case IfStatementSyntax ifStatement:
                    keyword = ifStatement.IfKeyword;
                    while (ifStatement.Else != null)
                    {
                        keyword = ifStatement.Else.ElseKeyword;
                        if (!(ifStatement.Else.Statement is IfStatementSyntax elseIf)) return ifStatement.Else.Statement;
                        ifStatement = elseIf;
                    }
                    return ifStatement.Statement;
                case ForStatementSyntax forStatement:
                    keyword = forStatement.ForKeyword;
                    return forStatement.Statement;
                case CommonForEachStatementSyntax forEach:
                    keyword = forEach.ForEachKeyword;
                    return forEach.Statement;
                default:
                    WhileStatementSyntax whileStatement = (WhileStatementSyntax)statement;
                    keyword = whileStatement.WhileKeyword;
                    return whileStatement.Statement;
            }
        }

        private static StatementSyntax NextStatement(StatementSyntax statement)
        {
            SyntaxList<StatementSyntax> siblings;
            switch (statement.Parent)
            {
                case BlockSyntax block: siblings = block.Statements; break;
                case SwitchSectionSyntax section: siblings = section.Statements; break;
                default: return null;
            }
            int index = siblings.IndexOf(statement);
            return index + 1 < siblings.Count ? siblings[index + 1] : null;
        }
    }
}