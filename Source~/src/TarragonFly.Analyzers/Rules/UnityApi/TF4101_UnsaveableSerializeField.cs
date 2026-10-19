using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.UnityApi
{
    // TF4101: [SerializeField] on a field of a type Unity can't save, as in
    // "[SerializeField] Dictionary<string, AudioClip> clips;" or "[SerializeField] IWeapon weapon;".
    //
    // Unity skips the field without a word: the Inspector doesn't show it, values set in code before
    // a save are gone after the next domain reload, and a prefab instance starts with an empty field.
    //
    // Types Unity can't save: dictionaries, HashSet and the other System.Collections.Generic types
    // apart from List<T>, multi-dimensional arrays, arrays or lists of arrays or lists, delegates,
    // Nullable<T>, and, without [SerializeReference], interfaces, abstract classes and object. Unity
    // objects are fine at any type, abstract ones included. The checks also look inside one array or
    // List<T>. Odin's serialized types (SerializedMonoBehaviour and the like) save what Unity can't,
    // so their fields are skipped. [field: SerializeField] on auto-properties counts. Reported on the
    // field or property name.
    //
    // Unity 6000.6 can save dictionaries and checks these fields itself (UAC1009, UAC1010 and the
    // dictionary rules), so this rule stays quiet when UNITY_6000_6_OR_NEWER is defined.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnsaveableSerializeFieldAnalyzer : ScopedAnalyzer
    {
        private const string UnityChecksItselfSymbol = "UNITY_6000_6_OR_NEWER";

        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF4101",
            "Serialized field of a type Unity can't save",
            "Unity can't save '{0}' ({1}), so its value never reaches the Inspector or the saved scene",
            "Unity",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            SyntaxTree anyTree = start.Compilation.SyntaxTrees.FirstOrDefault();
            if (anyTree == null || anyTree.Options.PreprocessorSymbolNames.Contains(UnityChecksItselfSymbol)) return;

            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (Frameworks.Odin.IsOdinSerializedType(type)) return;
                foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
                {
                    if (field.IsStatic || !SymbolNames.HasAttribute(field, "UnityEngine.SerializeField")) continue;
                    bool serializeReference = SymbolNames.HasAttribute(field, "UnityEngine.SerializeReference");
                    string problem = WhyUnityCantSave(field.Type, serializeReference, insideCollection: false);
                    if (problem == null) continue;

                    ISymbol declared = (ISymbol)(field.AssociatedSymbol as IPropertySymbol) ?? field;
                    Location location = declared.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location == null || !scope.IsAnalyzed(location.SourceTree)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, declared.Name, problem));
                }
            }, SymbolKind.NamedType);
        }

        // Why Unity can't save the type, or null when it can.
        private static string WhyUnityCantSave(ITypeSymbol type, bool serializeReference, bool insideCollection)
        {
            if (type == null || type.TypeKind == TypeKind.TypeParameter || type.TypeKind == TypeKind.Error) return null;
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank > 1) return "a multi-dimensional array";
                if (insideCollection) return "an array or list inside an array or list";
                return WhyUnityCantSave(array.ElementType, serializeReference, insideCollection: true);
            }
            if (Frameworks.Unity.IsUnityObject(type)) return null;

            INamedTypeSymbol named = type as INamedTypeSymbol;
            if (named != null && SymbolNames.IsNamed(named.OriginalDefinition, "System.Collections.Generic.List`1"))
            {
                if (insideCollection) return "an array or list inside an array or list";
                return WhyUnityCantSave(named.TypeArguments[0], serializeReference, insideCollection: true);
            }
            if (named != null && SymbolNames.IsInNamespace(named.OriginalDefinition, "System.Collections.Generic")) return $"a {named.Name}";
            if (named != null && SymbolNames.IsNamed(named.OriginalDefinition, "System.Nullable`1")) return "a Nullable<T>";
            if (type.TypeKind == TypeKind.Delegate) return "a delegate";
            if (serializeReference) return null;
            if (type.TypeKind == TypeKind.Interface) return "an interface without [SerializeReference]";
            if (type.SpecialType == SpecialType.System_Object) return "object without [SerializeReference]";
            if (type.IsAbstract && type.TypeKind == TypeKind.Class) return "an abstract class without [SerializeReference]";
            return null;
        }
    }
}