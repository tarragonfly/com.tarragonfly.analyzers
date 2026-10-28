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
    // TF1002: code left in a comment.
    //
    // Commented-out code goes stale fast: nobody compiles it, so when someone uncomments it months
    // later it no longer fits. Git already keeps the old version. Delete it.
    //
    // A comment line counts as code when, without its comment markers, it is:
    //   - a lone "{", "}", "};", ");" or "});";
    //   - an if, else if, for, foreach, while, switch, catch, using or lock header such as "if (dead)",
    //     also when its condition goes on over the next comment lines;
    //   - "else", "try", "finally" or "do" followed by "{", or any other line ending in "{" whose start
    //     parses as a statement or member header;
    //   - a line ending in ";" or "}" that parses as statements, one member or a using directive, as
    //     in "Reset(); Play();" or "if (dead) { Respawn(); }";
    //   - a whole block after catch, finally, else, try, do, get, set or init, as in
    //     "catch { return false; }", parsed next to the statement or property it belongs to;
    //   - the rest of an expression broken before an operator, as in "?? Create(name);", "&& isDone;"
    //     or "== null;";
    //   - the rest of a list of enum members or initializer items, as in ", MaskTex = 4 };" after an
    //     enum;
    //   - the end of a call split over lines: a line ending in ");" with more ")" than "(", whose text
    //     without those extra ")" parses as arguments, as in "index));" or ", (GraphElement)node);",
    //     or as the end of an expression broken before a ?:, &&, ||, ??, =>, as or member access, as
    //     in ": 0f);" or ".ToList());".
    // A statement made of an expression counts when it's a call, assignment, ++ or --, await, "new" or
    // a lambda, or a leftover such as "+ offset;", "count;", "desc.clearDepth;" or "!taken &&
    // drinks.Count > 0;". A bare "this.speed;", ?:, is and as read as prose, and so does a formula of
    // three or more terms, such as "a + b + c;", unless it calls something or holds a string. A
    // dotted name counts even when it's really a note, such as
    // "UniversalRenderPipeline.maxVisibleAdditionalLights;" after a field. XML documentation comments
    // are never checked, also when Unity compiles without parsing them and their /// lines arrive as
    // plain comments.
    // Consecutive // lines, even with blank lines or #if lines between them, form one comment and get
    // one warning at their first code line; each /* */ comment stands alone. In a multi-line /* */
    // comment the warning covers the whole line.
    //
    // Edge cases:
    //   - A line ending in ":" ("Usage:", "Example:") starts an example, so the rest of that comment
    //     is not checked; a "case 2:" or "default:" line doesn't, though it isn't code by itself.
    //   - A labeled statement such as "Example: player.Restore(10);" is prose, not code, and so is a
    //     line led by a longer phrase and a colon, such as "skipped porting: Reset();".
    //   - A lone declaration of a capitalized name, "Index Count;", reads as prose and isn't code,
    //     and neither is a line of several statements where one is a bare declaration, as in "One
    //     sentence; another sentence;".
    //   - "foreach (...)" headers count, like the other loop headers.
    //   - Lines are parsed rather than matched on their last character, so prose that happens to end
    //     in a semicolon or a brace isn't reported, and neither are lines from other languages that
    //     C# can't parse, such as Java's "continue outer;".
    //   - Code followed by a second // comment counts when the whole line still ends like code, as in
    //     "Log(frame); // + count);". With a plain note at the end, as in "return; // early out", the
    //     line reads as a remark and isn't reported.
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
        private static readonly string[] ContinuationOperators = { "?", "=>", "==", "!=", "<", ">", "+", "-", "*", "/", "%", "&", "|", "^", "as ", "is ", "." };

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
                if (item.IsKind(SyntaxKind.SingleLineCommentTrivia) && !item.ToString().StartsWith("///", StringComparison.Ordinal))
                {
                    lineComments.Add(item);
                    continue;
                }
                if (item.IsKind(SyntaxKind.WhitespaceTrivia) || item.IsKind(SyntaxKind.EndOfLineTrivia) || item.IsDirective) continue;

                ReportLineComments(context, lineComments);
                if (item.IsKind(SyntaxKind.MultiLineCommentTrivia)) ReportBlockComment(context, item, text);
            }
            ReportLineComments(context, lineComments);
        }

        private static void ReportLineComments(SyntaxTreeAnalysisContext context, List<SyntaxTrivia> comments)
        {
            List<string> lines = comments.Select(comment => comment.ToString().Substring(2).Trim()).ToList();
            int first = FirstCodeLine(lines);
            if (first >= 0) context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Tree, comments[first].Span)));
            comments.Clear();
        }

        // The index of the first code line, or -1 when there's none before an example starts.
        private static int FirstCodeLine(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (StartsExample(lines[i])) return -1;
                if (IsCode(lines[i]) || IsHeaderOverLines(lines, i)) return i;
            }
            return -1;
        }

        private static void ReportBlockComment(SyntaxTreeAnalysisContext context, SyntaxTrivia comment, SourceText text)
        {
            string content = comment.ToString();
            if (content.StartsWith("/**", StringComparison.Ordinal) && !content.StartsWith("/**/", StringComparison.Ordinal)) return;

            LinePositionSpan lines = text.Lines.GetLinePositionSpan(comment.Span);
            List<string> lineTexts = new List<string>();
            List<TextSpan> spans = new List<TextSpan>();
            for (int number = lines.Start.Line; number <= lines.End.Line; number++)
            {
                TextLine textLine = text.Lines[number];
                int from = Math.Max(textLine.Start, comment.Span.Start);
                int to = Math.Min(textLine.End, comment.Span.End);
                lineTexts.Add(BlockCommentLine(text.ToString(TextSpan.FromBounds(from, to))));
                // A one-line /* */ comment is reported whole; inside a longer one, the whole line is.
                spans.Add(lines.Start.Line == lines.End.Line ? comment.Span : TextSpan.FromBounds(number == lines.Start.Line ? from : textLine.Start, textLine.End));
            }
            int first = FirstCodeLine(lineTexts);
            if (first >= 0) context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(context.Tree, spans[first])));
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

        // "Usage:" starts an example; a switch label such as "case 2:" doesn't.
        private static bool StartsExample(string line)
        {
            if (line == "default:" || line.StartsWith("case ", StringComparison.Ordinal)) return false;
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
            if (line.EndsWith(");", StringComparison.Ordinal) && line.Count(c => c == ')') > line.Count(c => c == '(') && IsCallTail(line)) return true;
            if (line.EndsWith("}", StringComparison.Ordinal) && ParsesAsKeywordBlock(line)) return true;
            if (line.EndsWith(";", StringComparison.Ordinal) && (IsStatementTail(line) || IsListTail(line))) return true;
            if (line.EndsWith(";", StringComparison.Ordinal) || line.EndsWith("}", StringComparison.Ordinal)) return ParsesAsStatementsOrMember(line);
            return false;
        }

        // "index));", ", (GraphElement)node);", ": 0f);" or ".ToList());": without the ")" it doesn't
        // open, what's left parses as arguments or as the rest of an expression broken before an
        // operator, so prose such as "see note (12));" or ": see above);" doesn't count.
        private static bool IsCallTail(string line)
        {
            string rest = line.Substring(0, line.Length - 1).TrimEnd();
            for (int extra = line.Count(c => c == ')') - line.Count(c => c == '('); extra > 0; extra--)
            {
                if (!rest.EndsWith(")", StringComparison.Ordinal)) return false;
                rest = rest.Substring(0, rest.Length - 1).TrimEnd();
            }
            if (rest.StartsWith(",", StringComparison.Ordinal)) rest = rest.Substring(1);
            rest = rest.Trim();
            if (rest.Length == 0) return false;
            return ParsesWithoutErrors(SyntaxFactory.ParseArgumentList("(" + rest + ")")) || ParseExpressionTail(rest) != null;
        }

        // "?? Create(name);" or "== null;": the rest of a statement broken before an operator. A
        // formula reads as prose here too, as in "-a + b + c;".
        private static bool IsStatementTail(string line)
        {
            ExpressionSyntax expression = ParseExpressionTail(line.Substring(0, line.Length - 1).Trim());
            return expression != null && !ContainsFormula(expression);
        }

        // ": 0f", "&& isDone" or "?? Create(name)" with the missing start put in front, or null when
        // that doesn't parse, or only parses as a pattern, as ": it is done (mostly)" does.
        private static ExpressionSyntax ParseExpressionTail(string rest)
        {
            string start = MissingExpressionStart(rest);
            if (start == null) return null;
            ExpressionSyntax expression = SyntaxFactory.ParseExpression(start + rest);
            return ParsesWithoutErrors(expression) && !expression.DescendantNodesAndSelf().OfType<IsPatternExpressionSyntax>().Any() ? expression : null;
        }

        // ", MaskTex = 4 };" after an enum, ", 4 };" after an array or ", [2] = 5 };" after an object
        // initializer: the rest of a list, closed on this line.
        private static bool IsListTail(string line)
        {
            if (!line.StartsWith(",", StringComparison.Ordinal) || !line.EndsWith("};", StringComparison.Ordinal)) return false;
            string items = line.Substring(1, line.Length - 3).Trim();
            if (items.Length == 0) return false;
            return ParsesWithoutErrors(SyntaxFactory.ParseMemberDeclaration("enum E { A, " + items + "\n}"))
                || ParsesWithoutErrors(SyntaxFactory.ParseExpression("new[] { a, " + items + "\n}"))
                || ParsesWithoutErrors(SyntaxFactory.ParseExpression("new A { " + items + "\n}"));
        }

        // "catch { return false; }", "else { health = 0; }" or "get { return health; }": a whole block
        // after a keyword, parsed next to the statement or property it belongs to.
        private static bool ParsesAsKeywordBlock(string line)
        {
            if (line.StartsWith("}", StringComparison.Ordinal)) line = line.Substring(1).Trim();
            switch (FirstWord(line))
            {
                case "catch":
                case "finally":
                    return ParsesWithoutErrors(SyntaxFactory.ParseStatement("try { }\n" + line + "\n"));
                case "else":
                    return ParsesWithoutErrors(SyntaxFactory.ParseStatement("if (a) { }\n" + line + "\n"));
                case "try":
                    return ParsesWithoutErrors(SyntaxFactory.ParseStatement(line + "\nfinally { }"));
                case "do":
                    return ParsesWithoutErrors(SyntaxFactory.ParseStatement(line + "\nwhile (a);"));
                case "get":
                case "set":
                case "init":
                    return ParsesWithoutErrors(SyntaxFactory.ParseMemberDeclaration("int P { " + line + "\n}"));
                default:
                    return false;
            }
        }

        // The letters a line starts with, such as "catch" in "catch (Exception e) { }".
        private static string FirstWord(string line)
        {
            int end = 0;
            while (end < line.Length && char.IsLetter(line[end])) end++;
            return line.Substring(0, end);
        }

        // What a line that carries on an expression needs in front of it to parse: "a ? b " for ": 0f"
        // and "a " for "&& done", "?? fallback", "=> x * 2", "as Enemy" or ".ToList()".
        private static string MissingExpressionStart(string rest)
        {
            if (rest.StartsWith(":", StringComparison.Ordinal)) return "a ? b ";
            return ContinuationOperators.Any(op => rest.StartsWith(op, StringComparison.Ordinal)) ? "a " : null;
        }

        // "if (health < 0 ||" with the rest of the condition on the following comment lines.
        private static bool IsHeaderOverLines(List<string> lines, int start)
        {
            string joined = lines[start];
            if (!HeaderKeywords.Any(keyword => joined.StartsWith(keyword + " (", StringComparison.Ordinal) || joined.StartsWith(keyword + "(", StringComparison.Ordinal))) return false;
            for (int next = start + 1; next < lines.Count && joined.Count(c => c == '(') > joined.Count(c => c == ')'); next++)
            {
                joined += " " + lines[next];
            }
            return joined.Count(c => c == '(') == joined.Count(c => c == ')') && IsHeader(joined);
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

        private static bool ParsesAsStatementsOrMember(string line)
        {
            if (ParsesAsStatements(line)) return true;
            MemberDeclarationSyntax member = SyntaxFactory.ParseMemberDeclaration(line);
            if (ParsesWithoutErrors(member)) return !(member is FieldDeclarationSyntax field && IsProseDeclaration(field.Modifiers, field.Declaration, true));
            CompilationUnitSyntax unit = SyntaxFactory.ParseCompilationUnit(line);
            return unit.Usings.Count == 1 && unit.Members.Count == 0 && ParsesWithoutErrors(unit);
        }

        // One or more statements, each one C# would take on its own line. The closing brace goes on its
        // own line, or a second // comment after the code, as in "Reset(); // and again;", would swallow it.
        private static bool ParsesAsStatements(string line)
        {
            if (!(SyntaxFactory.ParseStatement("{ " + line + "\n}") is BlockSyntax block) || !ParsesWithoutErrors(block) || block.Statements.Count == 0) return false;
            bool alone = block.Statements.Count == 1;
            foreach (StatementSyntax statement in block.Statements)
            {
                if (!IsCodeStatement(statement, alone)) return false;
            }
            return true;
        }

        // "Example: player.Restore(10);" parses as a labeled statement, but it's prose introducing code.
        private static bool ParsesAsStatement(string text)
        {
            StatementSyntax statement = SyntaxFactory.ParseStatement(text);
            return ParsesWithoutErrors(statement) && IsCodeStatement(statement, true);
        }

        private static bool IsCodeStatement(StatementSyntax statement, bool alone)
        {
            switch (statement)
            {
                case LabeledStatementSyntax _:
                    return false;
                case ExpressionStatementSyntax expression:
                    return IsCodeExpression(expression.Expression);
                case LocalDeclarationStatementSyntax local:
                    return !IsProseDeclaration(local.Modifiers, local.Declaration, alone);
                default:
                    return true;
            }
        }

        // The expressions C# takes as a statement, a lambda, or what's left of a line cut short, such as
        // "+ offset", "desc.clearDepth" or "!taken && drinks.Count > 0". A bare "this.speed", ?:, is, as
        // and formulas such as "a + b + c" read as prose.
        private static bool IsCodeExpression(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case InvocationExpressionSyntax _:
                case AssignmentExpressionSyntax _:
                case AwaitExpressionSyntax _:
                case BaseObjectCreationExpressionSyntax _:
                case LambdaExpressionSyntax _:
                case PrefixUnaryExpressionSyntax _:
                case PostfixUnaryExpressionSyntax _:
                case IdentifierNameSyntax _:
                case LiteralExpressionSyntax _:
                case ElementAccessExpressionSyntax _:
                case MemberBindingExpressionSyntax _:
                case TypeOfExpressionSyntax _:
                    return true;
                case MemberAccessExpressionSyntax access:
                    return !(access.Expression is ThisExpressionSyntax);
                case ParenthesizedExpressionSyntax parenthesized:
                    return IsCodeExpression(parenthesized.Expression);
                case ConditionalAccessExpressionSyntax conditional:
                    return IsCodeExpression(conditional.WhenNotNull);
                case BinaryExpressionSyntax binary:
                    return !binary.IsKind(SyntaxKind.IsExpression) && !binary.IsKind(SyntaxKind.AsExpression) && !ContainsFormula(binary);
                default:
                    return false;
            }
        }

        // "a + b + c", "speed * time * scale" or the "a + b + c" in "a + b + c > 0": three or more terms
        // joined by arithmetic or bitwise operators, with no call or string anywhere in the expression.
        private static bool ContainsFormula(ExpressionSyntax expression)
        {
            if (expression.DescendantNodesAndSelf().Any(node => node is InvocationExpressionSyntax || node is InterpolatedStringExpressionSyntax || node.IsKind(SyntaxKind.StringLiteralExpression))) return false;
            return expression.DescendantNodesAndSelf().OfType<BinaryExpressionSyntax>()
                .Any(binary => IsArithmeticOrBitwise(binary) && !(binary.Parent is BinaryExpressionSyntax parent && IsArithmeticOrBitwise(parent)) && FormulaTermCount(binary) >= 3);
        }

        private static int FormulaTermCount(ExpressionSyntax expression)
        {
            return expression is BinaryExpressionSyntax binary && IsArithmeticOrBitwise(binary) ? FormulaTermCount(binary.Left) + FormulaTermCount(binary.Right) : 1;
        }

        private static bool IsArithmeticOrBitwise(BinaryExpressionSyntax binary)
        {
            switch (binary.Kind())
            {
                case SyntaxKind.AddExpression:
                case SyntaxKind.SubtractExpression:
                case SyntaxKind.MultiplyExpression:
                case SyntaxKind.DivideExpression:
                case SyntaxKind.ModuloExpression:
                case SyntaxKind.BitwiseAndExpression:
                case SyntaxKind.BitwiseOrExpression:
                case SyntaxKind.ExclusiveOrExpression:
                case SyntaxKind.LeftShiftExpression:
                case SyntaxKind.RightShiftExpression:
                    return true;
                default:
                    return false;
            }
        }

        // "Index Count;" and the "One sentence;" in "One sentence; another sentence;": a plain type name
        // and one variable with nothing else. Alone on its line it reads as prose only when the name
        // is capitalized, since "Rigidbody body;" is a real declaration.
        private static bool IsProseDeclaration(SyntaxTokenList modifiers, VariableDeclarationSyntax declaration, bool alone)
        {
            if (modifiers.Count > 0 || !(declaration.Type is IdentifierNameSyntax) || declaration.Variables.Count != 1) return false;
            VariableDeclaratorSyntax variable = declaration.Variables[0];
            if (variable.Initializer != null || variable.ArgumentList != null) return false;
            return !alone || char.IsUpper(variable.Identifier.ValueText[0]);
        }

        // The parsers turn any text left over into an error, so no errors means the whole line parsed.
        private static bool ParsesWithoutErrors(SyntaxNode node)
        {
            return node != null && !node.ContainsDiagnostics;
        }
    }
}