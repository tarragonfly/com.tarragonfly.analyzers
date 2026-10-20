using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1022: an unbraced body of an if, else, for, foreach, while or do that sits on the next line but
    // isn't indented past its keyword.
    //
    //     if (isPaused)
    //     return;            // reads like the next statement, but only runs when paused
    //
    // An if on the line after its else is the else's body, so it needs indenting past the else too;
    // its own body is then checked against the if. Tabs count as 4 columns. Reported on the statement's
    // header, from its keyword to the closing parenthesis (from "else" for an else if).
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnindentedBodyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1022",
            "Unbraced body not indented",
            "The body of this statement isn't indented past it, so it reads as the next statement; indent it or add braces",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(Analyze),
                SyntaxKind.IfStatement, SyntaxKind.ElseClause, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement,
                SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement, SyntaxKind.DoStatement);
        }

        private static void Analyze(SyntaxNodeAnalysisContext context)
        {
            SyntaxToken keyword;
            SyntaxToken headerEnd;
            StatementSyntax body;
            switch (context.Node)
            {
                case IfStatementSyntax ifStatement:
                    if (!Branches.IsChainStart(ifStatement) && !IsBelowItsElse(ifStatement)) return;
                    keyword = ifStatement.IfKeyword;
                    headerEnd = ifStatement.CloseParenToken;
                    body = ifStatement.Statement;
                    break;
                case ElseClauseSyntax elseClause:
                    keyword = elseClause.ElseKeyword;
                    if (elseClause.Statement is IfStatementSyntax elseIf && !IsBelowItsElse(elseIf))
                    {
                        headerEnd = elseIf.CloseParenToken;
                        body = elseIf.Statement;
                    }
                    else
                    {
                        headerEnd = keyword;
                        body = elseClause.Statement;
                    }
                    break;
                case ForStatementSyntax forStatement:
                    keyword = forStatement.ForKeyword;
                    headerEnd = forStatement.CloseParenToken;
                    body = forStatement.Statement;
                    break;
                case CommonForEachStatementSyntax forEach:
                    keyword = forEach.ForEachKeyword;
                    headerEnd = forEach.CloseParenToken;
                    body = forEach.Statement;
                    break;
                case WhileStatementSyntax whileStatement:
                    keyword = whileStatement.WhileKeyword;
                    headerEnd = whileStatement.CloseParenToken;
                    body = whileStatement.Statement;
                    break;
                default:
                    DoStatementSyntax doStatement = (DoStatementSyntax)context.Node;
                    keyword = doStatement.DoKeyword;
                    headerEnd = keyword;
                    body = doStatement.Statement;
                    break;
            }
            if (body is BlockSyntax) return;

            int headerEndLine = headerEnd.GetLocation().GetLineSpan().EndLinePosition.Line;
            int bodyLine = body.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (bodyLine == headerEndLine || VisualColumns.Of(body) > VisualColumns.Of(keyword)) return;

            TextSpan header = TextSpan.FromBounds(keyword.SpanStart, headerEnd.Span.End);
            context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Node.SyntaxTree, header)));
        }

        // "else" with "if (ready)" starting on a line below it, rather than "else if (ready)".
        private static bool IsBelowItsElse(IfStatementSyntax ifStatement)
        {
            if (!(ifStatement.Parent is ElseClauseSyntax elseClause)) return false;
            return ifStatement.IfKeyword.GetLocation().GetLineSpan().StartLinePosition.Line > elseClause.ElseKeyword.GetLocation().GetLineSpan().StartLinePosition.Line;
        }
    }
}