using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2052: a class whose constructors are all private and that never creates itself.
    //
    // Nothing can ever create it, so its instance members are dead code, or a factory method is
    // missing.
    //
    // A class creates itself with "new ItsName(...)" or "new()" anywhere inside it, nested types
    // included. A nested subclass counts too, wherever it gets created, and so does its ": base()",
    // since both run the private constructor. A class with no instance members but static members or
    // nested types is a holder for those and fine. Static classes and records aren't checked; abstract ones are, since private
    // constructors also stop anything from deriving. Reported on the class name.
    //
    // Unity objects aren't checked, since Unity creates MonoBehaviours and ScriptableObjects itself,
    // private constructor or not.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnconstructibleClassAnalyzer : DiagnosticAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2052",
            "Class that can't be instantiated",
            "'{0}' has only private constructors and nothing inside that uses them, so it can never be created",
            "LikelyBugs",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            // Generated factories count as uses, but generated classes still receive no diagnostics.
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
            context.RegisterCompilationStartAction(start =>
            {
                CompilationScope scope = CompilationScope.Get(start.Compilation, start.Options, start.CancellationToken);
                if (scope.Checks(Rule)) RegisterChecks(start, scope);
            });
        }

        private static void RegisterChecks(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<INamedTypeSymbol, bool> candidates = new ConcurrentDictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<INamedTypeSymbol, bool> constructorUsed = new ConcurrentDictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);
            start.RegisterOperationAction(context =>
            {
                IObjectCreationOperation creation = (IObjectCreationOperation)context.Operation;
                for (INamedTypeSymbol owner = context.ContainingSymbol.ContainingType; owner != null; owner = owner.ContainingType)
                {
                    if (SymbolEqualityComparer.Default.Equals(creation.Type?.OriginalDefinition, owner.OriginalDefinition)) constructorUsed[owner.OriginalDefinition] = true;
                }
                if (creation.Type is INamedTypeSymbol created) MarkOuterBaseClassesUsed(created, constructorUsed);
            }, OperationKind.ObjectCreation);
            start.RegisterOperationAction(context =>
            {
                IInvocationOperation call = (IInvocationOperation)context.Operation;
                if (call.TargetMethod.MethodKind != MethodKind.Constructor || !(context.ContainingSymbol is IMethodSymbol constructor)
                    || constructor.MethodKind != MethodKind.Constructor || SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, constructor.ContainingType)) return;
                MarkOuterBaseClassesUsed(constructor.ContainingType, constructorUsed);
            }, OperationKind.Invocation);
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (type.TypeKind != TypeKind.Class || type.IsStatic || type.IsRecord || Frameworks.Unity.IsUnityObject(type)) return;

                foreach (IMethodSymbol constructor in type.InstanceConstructors)
                {
                    if (constructor.IsImplicitlyDeclared || constructor.DeclaredAccessibility != Accessibility.Private) return;
                }
                bool instanceMembers = type.GetMembers().Any(member => !member.IsStatic && !member.IsImplicitlyDeclared
                    && !(member is IMethodSymbol method && method.MethodKind == MethodKind.Constructor) && !(member is INamedTypeSymbol));
                bool holder = !instanceMembers && type.GetMembers().Any(member => (member.IsStatic && !member.IsImplicitlyDeclared) || member is INamedTypeSymbol);
                if (!holder) candidates[type] = true;
            }, SymbolKind.NamedType);
            start.RegisterCompilationEndAction(context =>
            {
                foreach (INamedTypeSymbol type in candidates.Keys)
                {
                    if (constructorUsed.ContainsKey(type)) continue;
                    Location location = type.Locations[0];
                    if (location.IsInSource && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
                }
            });
        }

        // A nested subclass runs one of its outer base class's constructors, private ones included.
        private static void MarkOuterBaseClassesUsed(INamedTypeSymbol derived, ConcurrentDictionary<INamedTypeSymbol, bool> constructorUsed)
        {
            for (INamedTypeSymbol owner = derived.ContainingType; owner != null; owner = owner.ContainingType)
            {
                for (INamedTypeSymbol baseType = derived.BaseType; baseType != null; baseType = baseType.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, owner.OriginalDefinition)) constructorUsed[owner.OriginalDefinition] = true;
                }
            }
        }
    }
}
