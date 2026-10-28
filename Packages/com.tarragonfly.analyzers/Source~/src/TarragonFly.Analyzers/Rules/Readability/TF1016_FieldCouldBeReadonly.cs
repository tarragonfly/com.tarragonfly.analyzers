using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1016: a private field that only its initializer or the constructors set, as in "private
    // List<Enemy> spawned = new List<Enemy>();" that is only ever added to.
    //
    // Marking it readonly says the reference never changes after construction, and the compiler then
    // stops a later edit from replacing it by accident.
    //
    // Writes in instance constructors and init accessors of the same type are fine, through "this" only
    // and not inside lambdas. Any other write rules the field out: an assignment, ++ or +=, or a ref or
    // out argument. Static fields, fields with attributes, fields nothing ever sets, and fields of
    // struct types other than the built-in ones (int, float, DateTime and so on) aren't checked; on a
    // struct such as Vector3, readonly turns member changes into changes to a copy. Pointers and
    // function pointers are checked like the built-in types. Fields set in Init(...), Awake or by
    // Unity serialization never qualify, since those aren't constructors and serialized fields carry
    // [SerializeField]. Reported on the field name.
    //
    // A field set in an init accessor counts as set during construction, since C# allows readonly
    // fields to be set there, and types split into several partial parts are checked too.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldCouldBeReadonlyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1016",
            "Field could be readonly",
            "'{0}' is only set while the object is constructed; make it readonly",
            "Readability",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<IFieldSymbol, bool> candidates = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<IFieldSymbol, bool> setDuringConstruction = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<IFieldSymbol, bool> setLater = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(context =>
            {
                IFieldSymbol field = (IFieldSymbol)context.Symbol;
                if (field.DeclaredAccessibility != Accessibility.Private || field.IsStatic || field.IsReadOnly || field.IsConst || field.IsImplicitlyDeclared) return;
                if (!field.GetAttributes().IsEmpty || field.ContainingType.TypeKind == TypeKind.Interface) return;
                if (IsStructOtherThanBuiltIn(field.Type)) return;
                candidates[field] = true;
                if (field.DeclaringSyntaxReferences.Any(r => r.GetSyntax(context.CancellationToken) is VariableDeclaratorSyntax declarator && declarator.Initializer != null))
                {
                    setDuringConstruction[field] = true;
                }
            }, SymbolKind.Field);

            start.RegisterOperationAction(context =>
            {
                IFieldReferenceOperation reference = (IFieldReferenceOperation)context.Operation;
                IFieldSymbol field = reference.Field.OriginalDefinition;
                if (field.IsStatic || field.IsReadOnly) return;
                if (!IsWrite(reference)) return;
                if (InConstruction(context.ContainingSymbol, reference, field)) setDuringConstruction[field] = true;
                else setLater[field] = true;
            }, OperationKind.FieldReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (IFieldSymbol field in candidates.Keys)
                {
                    if (!setDuringConstruction.ContainsKey(field) || setLater.ContainsKey(field)) continue;
                    Location location = field.Locations[0];
                    if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                }
            });
        }

        private static bool IsStructOtherThanBuiltIn(ITypeSymbol type)
        {
            if (!type.IsValueType || type.SpecialType != SpecialType.None) return false;
            return type.TypeKind != TypeKind.Enum && type.TypeKind != TypeKind.Pointer && type.TypeKind != TypeKind.FunctionPointer;
        }

        private static bool IsWrite(IFieldReferenceOperation reference)
        {
            if (reference.Syntax.Parent is RefExpressionSyntax) return true;
            IOperation parent = reference.Parent;
            switch (parent)
            {
                case IAssignmentOperation assignment: return assignment.Target == reference;
                case IIncrementOrDecrementOperation _: return true;
                case IArgumentOperation argument: return argument.Parameter != null && argument.Parameter.RefKind != RefKind.None && argument.Parameter.RefKind != RefKind.In;
                case IAddressOfOperation _: return true;
                case ITupleOperation tuple: return tuple.Parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == tuple;
                default: return false;
            }
        }

        private static bool InConstruction(ISymbol containing, IFieldReferenceOperation reference, IFieldSymbol field)
        {
            if (!(containing is IMethodSymbol method) || !SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, field.ContainingType)) return false;
            bool constructs = (method.MethodKind == MethodKind.Constructor && !method.IsStatic) || method.IsInitOnly;
            if (!constructs || !(reference.Instance is IInstanceReferenceOperation)) return false;
            for (IOperation parent = reference.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation || parent is ILocalFunctionOperation) return false;
            }
            return true;
        }
    }
}
