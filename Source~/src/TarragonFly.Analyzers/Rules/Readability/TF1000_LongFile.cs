using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1000: a file with more lines of code than max_file_lines (default 1000). Blank lines,
    // comment-only lines and preprocessor lines don't count.
    //
    // A file that long usually does several jobs, which makes any one behavior slow to find and
    // risky to change. Split it by responsibility; partial classes only hide the problem.
    //
    // Reported at the very start of the file.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LongFileAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1000",
            "File is too long",
            "This file has {0} lines of code, more than the limit of {1}; split it by responsibility",
            "Readability",
            CodeKind.All,
            description: "Counts lines that hold code, so blank and comment-only lines are free. The limit is max_file_lines in the settings file.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxFileLines;
            start.RegisterSyntaxTreeAction(scope.OnlyAnalyzedTrees(context =>
            {
                SyntaxNode root = context.Tree.GetRoot(context.CancellationToken);
                int lines = CodeLines.Count(root.DescendantTokens(), context.Tree.GetText(context.CancellationToken));
                if (lines <= limit) return;
                Location start = Location.Create(context.Tree, new TextSpan(0, 0));
                context.ReportDiagnostic(Diagnostic.Create(Rule, start, lines, limit));
            }));
        }
    }
}