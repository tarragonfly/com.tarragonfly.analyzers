using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1027: a character outside ASCII anywhere in a source file: an em dash in a comment, an
    // accented letter in a string, a non-breaking space in the indentation, an emoji in a log line.
    //
    // Game text belongs in localization tables, not in C#. The rest of a script has no use for such
    // characters: an editor that saves in the wrong encoding turns them into mojibake, a diff shows
    // the line as changed, and a lookalike character is hard to spot by eye. When a character is
    // meant, the escape "\u00E9" says so in a way every tool reads the same.
    //
    // Off until ascii_only = true is set. Every character above U+007F counts, in identifiers,
    // comments, string, char and interpolated string literals, directives and whitespace alike. One
    // warning per line, on the first such character, with its code point in the message; a pair of
    // surrogates is reported as the one character it encodes. Files matching a glob in
    // non_ascii_allowed_in, such as "Assets/Game/Dialogue/*", are left alone, and so are generated
    // files.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NonAsciiCharacterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1027",
            "Non-ASCII character in source",
            "Line contains a non-ASCII character (U+{0}); write it as an escape or replace it",
            "Readability",
            CodeKind.All,
            description: "Off until ascii_only = true is set in the settings file. Files matching a path glob in non_ascii_allowed_in are left alone.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            if (!scope.Settings.AsciiOnly) return;
            ImmutableArray<string> allowedPaths = ImmutableArray.CreateRange(scope.Settings.NonAsciiAllowedIn, path => path.Replace('\\', '/'));

            start.RegisterSyntaxTreeAction(scope.OnlyAnalyzedTrees(context =>
            {
                if (Glob.MatchesAny(allowedPaths, ProjectPaths.ToProjectRelative(context.Tree.FilePath))) return;
                SourceText text = context.Tree.GetText(context.CancellationToken);
                foreach (TextLine line in text.Lines)
                {
                    ReportFirstNonAscii(context, text, line);
                }
            }));
        }

        private static void ReportFirstNonAscii(SyntaxTreeAnalysisContext context, SourceText text, TextLine line)
        {
            // Past line.End sit the line break characters; \r and \n are ASCII, the Unicode ones aren't.
            for (int i = line.Start; i < line.EndIncludingLineBreak; i++)
            {
                char character = text[i];
                if (character <= '\x7F') continue;

                bool isPair = char.IsHighSurrogate(character) && i + 1 < line.EndIncludingLineBreak && char.IsLowSurrogate(text[i + 1]);
                int codePoint = isPair ? char.ConvertToUtf32(character, text[i + 1]) : character;
                Location location = Location.Create(context.Tree, new TextSpan(i, isPair ? 2 : 1));
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, codePoint.ToString("X4", CultureInfo.InvariantCulture)));
                return;
            }
        }
    }
}
