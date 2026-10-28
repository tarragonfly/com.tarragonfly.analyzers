using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6500: a bare [Service] on a component or ScriptableObject with serialized fields, as in
    // "[Service(typeof(IAudioService))] class AudioManager : MonoBehaviour" with
    // "[SerializeField] AudioMixer mixer;".
    //
    // With nowhere to get the instance from, Init(args) creates a new one: a new GameObject with the
    // component added, or ScriptableObject.CreateInstance. That instance has none of the values set
    // in the Inspector, so the mixer is null and the service fails at its first call, while the
    // configured object in the bootstrap scene is never used. Say where the instance lives with
    // LoadScene, FindFromScene, ResourcePath or AddressableKey.
    //
    // Serialized fields are the ones Unity saves (public, [SerializeField] or [SerializeReference]),
    // on the type or its base types. Reported on the attribute.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ServiceLosesInspectorValuesAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6500",
            "Service creates a new instance without its Inspector values",
            "Init(args) will create a new '{0}' without the values set in the Inspector, such as '{1}'; set LoadScene, FindFromScene, ResourcePath or AddressableKey",
            "InitArgs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (!SymbolNames.IsOrDerivesFrom(type, "UnityEngine.Component") && !Frameworks.Unity.IsScriptableObject(type)) return;
                IFieldSymbol serializedField = FirstSerializedField(type);
                if (serializedField == null) return;
                foreach (AttributeData attribute in ServiceAttributes.On(type))
                {
                    if (!Frameworks.InitArgs.CreatesNewInstance(attribute)) continue;
                    Location location = attribute.ApplicationSyntaxReference.GetSyntax(context.CancellationToken).GetLocation();
                    if (!scope.IsAnalyzed(location.SourceTree)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name, serializedField.AssociatedSymbol?.Name ?? serializedField.Name));
                }
            }, SymbolKind.NamedType);
        }

        private static IFieldSymbol FirstSerializedField(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null && !SymbolNames.IsInNamespace(current, "UnityEngine"); current = current.BaseType)
            {
                IFieldSymbol serialized = current.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(Frameworks.Unity.IsSerializedByUnity);
                if (serialized != null) return serialized;
            }
            return null;
        }
    }
}