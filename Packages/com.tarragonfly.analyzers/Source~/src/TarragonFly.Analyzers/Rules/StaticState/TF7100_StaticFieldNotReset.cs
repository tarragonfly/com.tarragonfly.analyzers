using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7100: a static field or auto-property in runtime code that changes during play and isn't reset
    // when Play Mode starts, as in "static int score;" with "score += points;" and no reset.
    //
    // With domain reload off (Enter Play Mode Options), statics keep their values from the last Play
    // Mode session: the score starts at the last run's total, a cached instance points at a destroyed
    // object, and a "first time" flag is already false. Reset the value in a static method marked
    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)].
    //
    // Checked: static fields that aren't const or readonly, and static auto-properties with a setter,
    // when code in this assembly writes them outside their initializer and static constructor. One that
    // only other assemblies could write isn't reported; TF7000 reports a static field that isn't
    // private. A reset is a write in a static method marked
    // [RuntimeInitializeOnLoadMethod] with SubsystemRegistration, AfterAssembliesLoaded,
    // BeforeSplashScreen or BeforeSceneLoad, or [InitializeOnEnterPlayMode]; anywhere in the
    // assembly counts. The default load type, AfterSceneLoad, runs after Awake and doesn't count.
    // Delegates and events are TF7101's job. Members marked, or in a type marked, with Unity 6000.6's
    // [AutoStaticsCleanup] or [NoAutoStaticsCleanup] are fine, and so are [ThreadStatic] fields, which hold
    // scratch state for the call that sets them. Reported on the name. Runtime code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StaticFieldNotResetAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7100",
            "Static value not reset when Play Mode starts",
            "'{0}' keeps its value into the next Play Mode session with domain reload off; reset it in a [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] method",
            "StaticState",
            CodeKind.Runtime,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<ISymbol, bool> candidates = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> changed = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> reset = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(context =>
            {
                ISymbol member = context.Symbol;
                if (!IsCandidate(member) || PlayModeResets.NeedsNoReset(member)) return;
                candidates[member] = true;
            }, SymbolKind.Field, SymbolKind.Property);

            start.RegisterOperationAction(context =>
            {
                ISymbol member = context.Operation is IFieldReferenceOperation field ? field.Field.OriginalDefinition
                    : (ISymbol)((IPropertyReferenceOperation)context.Operation).Property.OriginalDefinition;
                if (!member.IsStatic || !PlayModeResets.IsWrite(context.Operation)) return;
                if (PlayModeResets.IsResetMethod(context.ContainingSymbol)) reset[member] = true;
                else if (!PlayModeResets.IsStaticInitialization(context.ContainingSymbol, member)) changed[member] = true;
            }, OperationKind.FieldReference, OperationKind.PropertyReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (ISymbol member in candidates.Keys)
                {
                    if (!changed.ContainsKey(member) || reset.ContainsKey(member)) continue;
                    Location location = member.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location != null && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, member.Name));
                }
            });
        }

        private static bool IsCandidate(ISymbol member)
        {
            if (!member.IsStatic || member.IsImplicitlyDeclared) return false;
            switch (member)
            {
                case IFieldSymbol field:
                    return !field.IsConst && !field.IsReadOnly && field.AssociatedSymbol == null && field.Type.TypeKind != TypeKind.Delegate;
                case IPropertySymbol property:
                    return property.SetMethod != null && property.Type.TypeKind != TypeKind.Delegate && IsAutoProperty(property);
                default:
                    return false;
            }
        }

        private static bool IsAutoProperty(IPropertySymbol property)
        {
            return property.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));
        }
    }
}