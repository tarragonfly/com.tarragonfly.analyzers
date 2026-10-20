using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2001: a class or struct that overrides Equals(object) but not GetHashCode(), or the other way
    // round.
    //
    // Dictionary keys and HashSet entries then go missing: two equal objects land in different
    // buckets and Contains or TryGetValue can't find the one that's there.
    //
    // Each type is judged on its own overrides, so a derived class that overrides only Equals is
    // reported even when its base overrides both. Records aren't checked. Reported on the type name.
    // Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EqualsWithoutGetHashCodeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2001",
            "Equals without GetHashCode",
            "'{0}' overrides {1} but not {2}; override both so dictionaries and sets find it",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (type.IsRecord || (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)) return;

                bool equals = false;
                bool hashCode = false;
                foreach (ISymbol member in type.GetMembers())
                {
                    if (!(member is IMethodSymbol method) || !method.IsOverride) continue;
                    if (method.Name == "Equals" && method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Object) equals = true;
                    if (method.Name == "GetHashCode" && method.Parameters.Length == 0) hashCode = true;
                }
                if (equals == hashCode) return;

                Location location = type.Locations[0];
                if (!location.IsInSource || !scope.IsAnalyzed(location.SourceTree)) return;
                context.ReportDiagnostic(equals
                    ? Diagnostic.Create(Rule, location, type.Name, "Equals", "GetHashCode")
                    : Diagnostic.Create(Rule, location, type.Name, "GetHashCode", "Equals"));
            }, SymbolKind.NamedType);
        }
    }
}