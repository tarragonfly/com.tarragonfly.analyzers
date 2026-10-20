using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Settings
{
    // TF0000: a line of the settings file that the analyzer cannot use, such as a misspelled key.
    // Reported in each assembly that has analyzed files, since the file reaches every compilation.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SettingsFileAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF0000",
            "Fix the analyzer settings file",
            "{0}",
            "Settings",
            CodeKind.All,
            description: "A line of Assets/Settings.TarragonFly.Analyzers.additionalfile could not be used, such as a misspelled key, " +
                "a line without '=' or a limit that is not a whole number. The setting keeps its default until the line is fixed.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(start =>
            {
                CompilationScope scope = CompilationScope.Get(start.Compilation, start.Options, start.CancellationToken);
                if (scope.Settings.File == null || !scope.HasAnalyzedFiles(start.Compilation)) return;

                start.RegisterAdditionalFileAction(fileContext =>
                {
                    if (fileContext.AdditionalFile.Path != scope.Settings.File.Path) return;
                    SourceText text = fileContext.AdditionalFile.GetText(fileContext.CancellationToken);
                    foreach (SettingsProblem problem in scope.Settings.Problems)
                    {
                        Location location = text == null
                            ? Location.None
                            : Location.Create(fileContext.AdditionalFile.Path, problem.Span, text.Lines.GetLinePositionSpan(problem.Span));
                        fileContext.ReportDiagnostic(Diagnostic.Create(Rule, location, problem.Message));
                    }
                });
            });
        }
    }
}