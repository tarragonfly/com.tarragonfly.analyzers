using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1002: code left in a comment.
    //
    // Commented-out code goes stale fast: nobody compiles it, so when someone uncomments it months
    // later it no longer fits. Git already keeps the old version. Delete it.
    //
    // A comment line counts as code when, without its comment markers, it is:
    //   - a lone "{", "}", "};", ");" or "});";
    //   - an if, else if, for, foreach, while, switch, catch, using or lock header such as "if (dead)";
    //   - "else", "try", "finally" or "do" followed by "{", or any other line ending in "{" whose start
    //     parses as a statement or member header;
    //   - a line ending in ";" that parses as exactly one statement, member or using directive.
    // XML documentation comments are never checked. Consecutive // lines, even with blank lines
    // between them, form one comment and get one warning at their first code line; each /* */ comment
    // stands alone. In a multi-line /* */ comment the warning covers the whole line.
    //
    // Edge cases:
    //   - A line ending in ":" ("Usage:", "Example:") starts an example, so the rest of that comment
    //     is not checked.
    //   - A labeled statement such as "Example: player.Restore(10);" is prose, not code.
    //   - "foreach (...)" headers count, like the other loop headers.
    //   - Lines are parsed rather than matched on their last character, so prose that happens to end
    //     in a semicolon or a brace stays quiet.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CommentedOutCodeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1002",
            "Commented-out code",
            "Remove this commented-out code; git keeps the old version",
            "Readability",
            CodeKind.All,
            description: "Code in comments is never compiled, so it goes stale. Lines that end in ':' introduce examples and are skipped.");

        private static readonly string[] LoneCodeLines = { "{", "}", "};", ");", "});" };
        private static readonly string[] HeaderKeywords = { "if", "else if", "for", "foreach", "while", "switch", "catch", "using", "lock" };
        private static readonly string[] BlockKeywords = { "else", "try", "finally", "do", "get", "set" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxTreeAction(scope.OnlyAnalyzedTrees(AnalyzeTree));
        }

        private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
        {
            SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);
            SourceText text = context.Tree.GetText(context.CancellationToken);
            foreach (SyntaxToken token in root.DescendantTokens(descendIntoTrivia: false))
            {
                if (token.HasLeadingTrivia) AnalyzeTrivia(context, token.LeadingTrivia, text);
                if (token.HasTrailingTrivia) AnalyzeTrivia(context, token.TrailingTrivia, text);
            }
        }

        private static void AnalyzeTrivia(SyntaxTreeAnalysisContext context, SyntaxTriviaList trivia, SourceText text)
        {
            List<SyntaxTrivia> lineComments = new List<SyntaxTrivia>();
            foreach (SyntaxTrivia item in trivia)
            {
                if (item.IsKind(SyntaxKind.SingleLineCommentTrivia))
                {
                    lineComments.Add(item);
                    continue;
                }
                if (item.IsKind(SyntaxKind.WhitespaceTrivia) || item.IsKind(SyntaxKind.EndOfLineTrivia)) continue;

                ReportLineComments(context, lineComments);
                if (item.IsKind(SyntaxKind.MultiLineCommentTrivia)) ReportBlockComment(context, item, text);
            }
            ReportLineComments(context, lineComments);
        }

        private static void ReportLineComments(SyntaxTreeAnalysisContext context, List<SyntaxTrivia> comments)
        {
            foreach (SyntaxTrivia comment in comments)
            {
                string line = comment.ToString().Substring(2).Trim();
                if (StartsExample(line)) break;
                if (!IsCode(line)) continue;
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Tree, comment.Span)));
                break;
            }
            comments.Clear();
        }

        private static void ReportBlockComment(SyntaxTreeAnalysisContext context, SyntaxTrivia comment, SourceText text)
        {
            string content = comment.ToString();
            if (content.StartsWith("/**", StringComparison.Ordinal) && !content.StartsWith("/**/", StringComparison.Ordinal)) return;

            LinePositionSpan lines = text.Lines.GetLinePositionSpan(comment.Span);
            for (int number = lines.Start.Line; number <= lines.End.Line; number++)
            {
                TextLine textLine = text.Lines[number];
                int from = Math.Max(textLine.Start, comment.Span.Start);
                int to = Math.Min(textLine.End, comment.Span.End);
                string line = BlockCommentLine(text.ToString(TextSpan.FromBounds(from, to)));
                if (StartsExample(line)) return;
                if (!IsCode(line)) continue;

                // A one-line /* */ comment is reported whole; inside a longer one, the whole line is.
                TextSpan span = lines.Start.Line == lines.End.Line ? comment.Span : TextSpan.FromBounds(number == lines.Start.Line ? from : textLine.Start, textLine.End);
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Tree, span)));
                return;
            }
        }

        private static string BlockCommentLine(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("/*", StringComparison.Ordinal)) trimmed = trimmed.Substring(2);
            if (trimmed.EndsWith("*/", StringComparison.Ordinal)) trimmed = trimmed.Substring(0, trimmed.Length - 2);
            trimmed = trimmed.Trim();
            if (trimmed.StartsWith("*", StringComparison.Ordinal)) trimmed = trimmed.Substring(1).Trim();
            return trimmed;
        }

        private static bool StartsExample(string line)
        {
            return line.Length > 1 && line.EndsWith(":", StringComparison.Ordinal);
        }

        private static bool IsCode(string line)
        {
            if (line.Length == 0) return false;
            foreach (string lone in LoneCodeLines)
            {
                if (line == lone) return true;
            }
            if (IsHeader(line)) return true;
            if (line.EndsWith("{", StringComparison.Ordinal)) return IsBlockStart(line.Substring(0, line.Length - 1).Trim());
            if (line.EndsWith(";", StringComparison.Ordinal)) return ParsesAsOneStatementOrMember(line);
            return false;
        }

        // "if (dead)", "foreach (Enemy enemy in enemies)": a keyword, a parenthesized part, nothing after.
        private static bool IsHeader(string line)
        {
            if (!line.EndsWith(")", StringComparison.Ordinal)) return false;
            foreach (string keyword in HeaderKeywords)
            {
                if (!line.StartsWith(keyword, StringComparison.Ordinal)) continue;
                string rest = line.Substring(keyword.Length).TrimStart();
                if (rest.StartsWith("(", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool IsBlockStart(string head)
        {
            if (head.StartsWith("}", StringComparison.Ordinal)) head = head.Substring(1).Trim();
            if (head.Length == 0 || IsHeader(head)) return true;
            foreach (string keyword in BlockKeywords)
            {
                if (head == keyword) return true;
            }
            string block = head + " { }";
            return ParsesAsStatement(block) || ParsesWithoutErrors(SyntaxFactory.ParseMemberDeclaration(block));
        }

        private static bool ParsesAsOneStatementOrMember(string line)
        {
            if (ParsesAsStatement(line) || ParsesWithoutErrors(SyntaxFactory.ParseMemberDeclaration(line))) return true;
            CompilationUnitSyntax unit = SyntaxFactory.ParseCompilationUnit(line);
            return unit.Usings.Count == 1 && unit.Members.Count == 0 && ParsesWithoutErrors(unit);
        }

        // "Example: player.Restore(10);" parses as a labeled statement, but it's prose introducing code.
        private static bool ParsesAsStatement(string text)
        {
            StatementSyntax statement = SyntaxFactory.ParseStatement(text);
            return !(statement is LabeledStatementSyntax) && ParsesWithoutErrors(statement);
        }

        // The parsers turn any text left over into an error, so no errors means the whole line parsed.
        private static bool ParsesWithoutErrors(SyntaxNode node)
        {
            return node != null && !node.ContainsDiagnostics;
        }
    }
}