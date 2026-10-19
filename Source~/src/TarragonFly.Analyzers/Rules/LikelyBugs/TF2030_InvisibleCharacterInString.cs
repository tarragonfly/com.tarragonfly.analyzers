using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2030: a string literal with a character you can't see typed straight into it: a control
    // character, a non-breaking or other unusual space, or a zero-width character.
    //
    // A non-breaking space pasted into a scenario ID, a USS class name or an input binding path looks
    // identical to a normal one, and the lookup silently fails. Write such characters as escapes
    // ("\u00A0") when they're meant.
    //
    // Checked: regular, verbatim and raw strings, and the text parts of interpolated strings. A tab,
    // carriage return or line feed is fine in verbatim and raw strings, which span lines; anywhere
    // else it is reported too. Escapes are fine. Character literals aren't checked. Reported on the
    // literal (or the interpolated text part), once.
    //
    // It skips verbatim strings; they're checked here, minus the line layout characters. Game code
    // only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InvisibleCharacterInStringAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2030",
            "Invisible character in a string",
            "This string contains an invisible character (U+{0}); write it as an escape if it's meant to be there",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxToken token = ((LiteralExpressionSyntax)context.Node).Token;
                bool spansLines = token.Text.StartsWith("@", StringComparison.Ordinal) || token.Text.StartsWith("\"\"\"", StringComparison.Ordinal);
                Check(context, token, spansLines);
            }), SyntaxKind.StringLiteralExpression);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InterpolatedStringTextSyntax part = (InterpolatedStringTextSyntax)context.Node;
                string startText = ((InterpolatedStringExpressionSyntax)part.Parent).StringStartToken.Text;
                bool spansLines = startText.Contains("@") || startText.Contains("\"\"\"");
                Check(context, part.TextToken, spansLines);
            }), SyntaxKind.InterpolatedStringText);
        }

        private static void Check(SyntaxNodeAnalysisContext context, SyntaxToken token, bool spansLines)
        {
            foreach (char character in token.Text)
            {
                if (!IsInvisible(character, spansLines)) continue;
                string code = ((int)character).ToString("X4", CultureInfo.InvariantCulture);
                context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation(), code));
                return;
            }
        }

        private static bool IsInvisible(char character, bool spansLines)
        {
            if (character == ' ') return false;
            if (spansLines && (character == '\t' || character == '\r' || character == '\n')) return false;
            switch (CharUnicodeInfo.GetUnicodeCategory(character))
            {
                case UnicodeCategory.Control:
                case UnicodeCategory.Format:
                case UnicodeCategory.SpaceSeparator:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
                default:
                    return false;
            }
        }
    }
}