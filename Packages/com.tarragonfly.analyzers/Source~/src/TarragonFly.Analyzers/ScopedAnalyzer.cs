using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers
{
    // Base for TF rules: skips assemblies none of the analyzer's rules check, and generated code.
    // Rules still call scope.IsAnalyzed(tree) before reporting, since one assembly can mix
    // analyzed and skipped folders.
    public abstract class ScopedAnalyzer : DiagnosticAnalyzer
    {
        public sealed override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(start =>
            {
                CompilationScope scope = CompilationScope.Get(start.Compilation, start.Options, start.CancellationToken);
                if (!scope.ChecksAny(SupportedDiagnostics)) return;
                OnCompilationStart(start, scope);
            });
        }

        protected abstract void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope);
    }
}