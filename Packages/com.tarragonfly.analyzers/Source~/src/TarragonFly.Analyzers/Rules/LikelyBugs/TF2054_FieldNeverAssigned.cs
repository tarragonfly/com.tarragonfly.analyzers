using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Rules.DeadCode;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2054: a private field or auto-property, or a field of a private nested type, the code reads
    // but nothing ever sets, as in "private AudioSource source;" in a MonoBehaviour that never assigns
    // it.
    //
    // It always holds its default: 0, false, or null, which throws on first use. In Unity this is
    // usually a field meant for the Inspector that is missing [SerializeField], so Unity never
    // fills it.
    //
    // Any assignment, ++, +=, ref or out argument, or initializer counts as setting it, and so does
    // handing the field out by ref, as in "ref Handle Color => ref color", which lets callers set it.
    // Fields with attributes, such as [SerializeField], [SerializeReference] or Odin's, are skipped, as
    // are fields in [Serializable] types, fields in [StructLayout] types, which can be filled by
    // reinterpreting memory, fields of struct types other than the built-in ones (a Vector3 is usable
    // as it is), and fields nothing reads. Static and readonly fields count. Reported on the field
    // name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldNeverAssignedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2054",
            "Field never assigned",
            "Nothing sets '{0}', so it always holds its default value; assign it, or add [SerializeField] if the Inspector should",
            "LikelyBugs",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<ISymbol, bool> candidates = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> assigned = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> read = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(context =>
            {
                IFieldSymbol field = (IFieldSymbol)context.Symbol;
                if (!FieldUses.IsCodeOnly(field) || IsNonBuiltInStruct(field.Type)) return;
                if (field.DeclaringSyntaxReferences.Any(r => r.GetSyntax(context.CancellationToken) is VariableDeclaratorSyntax declarator && declarator.Initializer != null)) return;
                candidates[field] = true;
            }, SymbolKind.Field);

            start.RegisterSymbolAction(context =>
            {
                IPropertySymbol property = (IPropertySymbol)context.Symbol;
                if (property.DeclaredAccessibility != Accessibility.Private || property.IsIndexer || !property.GetAttributes().IsEmpty || IsNonBuiltInStruct(property.Type)) return;
                if (property.Locations.Length == 0 || !property.Locations[0].IsInSource) return;
                if (!(property.DeclaringSyntaxReferences.Length == 1 && property.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax declaration)) return;
                bool auto = declaration.Initializer == null && declaration.AccessorList != null
                    && declaration.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null);
                if (auto) candidates[property] = true;
            }, SymbolKind.Property);

            start.RegisterOperationAction(context =>
            {
                IPropertyReferenceOperation reference = (IPropertyReferenceOperation)context.Operation;
                IPropertySymbol property = reference.Property.OriginalDefinition;
                bool assignment = reference.Parent is IAssignmentOperation write && write.Target == reference;
                if (assignment || reference.Parent is IIncrementOrDecrementOperation) assigned[property] = true;
                else read[property] = true;
            }, OperationKind.PropertyReference);

            start.RegisterOperationAction(context =>
            {
                IFieldReferenceOperation reference = (IFieldReferenceOperation)context.Operation;
                IFieldSymbol field = reference.Field.OriginalDefinition;
                bool handedOutByRef = reference.Syntax.Parent is RefExpressionSyntax;
                if (FieldUses.IsPlainWrite(reference) || FieldUses.IsReadModifyWrite(reference) || reference.Parent is IAddressOfOperation || handedOutByRef) assigned[field] = true;
                else read[field] = true;
            }, OperationKind.FieldReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (ISymbol field in candidates.Keys)
                {
                    if (assigned.ContainsKey(field) || !read.ContainsKey(field)) continue;
                    Location location = field.Locations[0];
                    if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                }
            });
        }

        private static bool IsNonBuiltInStruct(ITypeSymbol type)
        {
            return type.IsValueType && type.SpecialType == SpecialType.None && type.TypeKind != TypeKind.Enum;
        }
    }
}