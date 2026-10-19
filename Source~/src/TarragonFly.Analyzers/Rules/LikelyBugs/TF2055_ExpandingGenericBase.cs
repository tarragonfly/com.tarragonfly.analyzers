using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2055: a generic class whose base class passes the class itself with different type arguments,
    // as in "class Node<T> : Base<Node<Node<T>>>".
    //
    // Each use needs an ever deeper type, so the runtime keeps loading new types until it fails with
    // a TypeLoadException, often only on device under IL2CPP.
    //
    // Passing the class with its own type parameters ("class Node<T> : Base<Node<T>>") is the normal
    // self-referencing pattern and fine. Only the base class is checked, not interfaces. Reported on
    // the class name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ExpandingGenericBaseAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2055",
            "Recursive generic inheritance",
            "The base class of '{0}' passes '{0}' itself with growing type arguments, which fails when the type loads",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (type.TypeKind != TypeKind.Class || !type.IsGenericType || type.BaseType == null) return;
                if (!Expands(type, type.BaseType)) return;

                Location location = type.Locations[0];
                if (location.IsInSource && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
            }, SymbolKind.NamedType);
        }

        // Whether "used" contains the type with arguments other than its own type parameters.
        private static bool Expands(INamedTypeSymbol type, ITypeSymbol used)
        {
            if (!(used is INamedTypeSymbol named)) return used is IArrayTypeSymbol array && Expands(type, array.ElementType);
            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, type.OriginalDefinition))
            {
                for (int i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (!SymbolEqualityComparer.Default.Equals(named.TypeArguments[i], type.TypeParameters[i])) return true;
                }
            }
            foreach (ITypeSymbol argument in named.TypeArguments)
            {
                if (Expands(type, argument)) return true;
            }
            return false;
        }
    }
}