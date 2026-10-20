using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6501: [Service(LoadScene = ...)] that doesn't say "DontDestroyOnLoad = false", as in
    // "[Service(typeof(IAudioService), LoadScene = "00_Bootstrap")]".
    //
    // Init(args) moves a service it loads from a scene into Unity's DontDestroyOnLoad scene unless told
    // otherwise. A bootstrap scene that stays loaded already keeps its services alive, and moving them
    // splits the bootstrap objects across two scenes, so scene-based lookups, additive unloads and
    // the Hierarchy stop matching how the game is set up. Write "DontDestroyOnLoad = false" so the
    // service stays in the scene that owns it.
    //
    // Reported on LoadScene when DontDestroyOnLoad is left out or set to true. Projects that want
    // their scene services moved can turn the rule off in their rule set.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SceneServiceDontDestroyOnLoadAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6501",
            "Scene-loaded service without DontDestroyOnLoad = false",
            "Add 'DontDestroyOnLoad = false' so Init(args) leaves '{0}' in the scene it loads it from",
            "InitArgs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                foreach (AttributeData attribute in ServiceAttributes.On(type))
                {
                    if (Frameworks.InitArgs.GetServiceArgument(attribute, "LoadScene") == null) continue;
                    if (Frameworks.InitArgs.GetServiceArgument(attribute, "DontDestroyOnLoad") is false) continue;
                    Location location = ServiceAttributes.ArgumentLocation(attribute, "LoadScene", context.CancellationToken);
                    if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
                }
            }, SymbolKind.NamedType);
        }
    }
}