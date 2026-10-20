using System.Collections.Immutable;
using System.Linq;
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
    // self-referencing pattern and fine, and so are those parameters swapped or repeated, as in
    // "class Pair<T, U> : Base<Pair<U, T>>", and closed types such as Node<int>: none of them grow.
    // Only the base class is checked, not interfaces. Reported on the class name. Game code only.
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

        // Whether "used" contains the type with an argument built from its own type parameters, as in
        // Node<Node<T>> or Node<T[]>, which makes each use one level deeper.
        private static bool Expands(INamedTypeSymbol type, ITypeSymbol used)
        {
            if (!(used is INamedTypeSymbol named)) return used is IArrayTypeSymbol array && Expands(type, array.ElementType);
            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, type.OriginalDefinition))
            {
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    if (!(argument is ITypeParameterSymbol) && Mentions(argument, type)) return true;
                }
            }
            foreach (ITypeSymbol argument in named.TypeArguments)
            {
                if (Expands(type, argument)) return true;
            }
            return false;
        }

        private static bool Mentions(ITypeSymbol used, INamedTypeSymbol type)
        {
            switch (used)
            {
                case ITypeParameterSymbol parameter: return type.TypeParameters.Contains(parameter, SymbolEqualityComparer.Default);
                case IArrayTypeSymbol array: return Mentions(array.ElementType, type);
                case INamedTypeSymbol named: return named.TypeArguments.Any(argument => Mentions(argument, type));
                default: return false;
            }
        }
    }
}