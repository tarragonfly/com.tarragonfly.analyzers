using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6502: "LazyInit = true" or "LoadAsync = true" on a service that comes from a scene, as in
    // "[Service(typeof(ISceneLoader), LoadScene = "00_Bootstrap", LazyInit = true)]".
    //
    // Either one changes when the service exists: lazily on the first request, or a few frames after
    // startup. A client whose Awake or Init runs before that gets nothing, so startup order starts to
    // depend on which scene Play Mode was entered from. That can be fine, but check the clients that
    // need the service at startup first; this rule is a reminder, so it reports at Info level.
    //
    // Scene services are those with LoadScene or FindFromScene. Reported on the LazyInit or LoadAsync
    // argument.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LazySceneServiceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6502",
            "Scene service created lazily or asynchronously",
            "'{0} = true' delays '{1}' past startup; check that no client needs it in Awake or Init before keeping it",
            "InitArgs",
            CodeKind.All,
            DiagnosticSeverity.Info);

        private static readonly string[] DelayingArguments = { "LazyInit", "LoadAsync" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                foreach (AttributeData attribute in ServiceAttributes.On(type))
                {
                    if (!Frameworks.InitArgs.IsSceneService(attribute)) continue;
                    foreach (string argument in DelayingArguments)
                    {
                        if (!(Frameworks.InitArgs.GetServiceArgument(attribute, argument) is true)) continue;
                        Location location = ServiceAttributes.ArgumentLocation(attribute, argument, context.CancellationToken);
                        if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, argument, type.Name));
                    }
                }
            }, SymbolKind.NamedType);
        }
    }
}