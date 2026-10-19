using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7003: a static field in a generic type (or a type nested in one) whose type doesn't use the
    // type parameters.
    //
    // Every closed type gets its own copy, so Pool<Bullet>.count and Pool<Enemy>.count are two
    // different counters, which is rarely what a shared counter or cache means. With domain reload
    // off, each copy also survives into the next Play Mode session.
    //
    // A field whose type uses a type parameter, such as List<T> or Func<T, int>, needs a copy per type
    // and is fine, as are constants. Reported on the field name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StaticFieldInGenericTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7003",
            "Static field in a generic type",
            "'{0}' gets a separate copy for every type argument of '{1}', and with domain reload off each copy carries over into the next Play Mode session",
            "StaticState",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                FieldDeclarationSyntax declaration = (FieldDeclarationSyntax)context.Node;
                foreach (VariableDeclaratorSyntax variable in declaration.Declaration.Variables)
                {
                    if (!(context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is IFieldSymbol field) || !field.IsStatic || field.IsConst) continue;
                    INamedTypeSymbol generic = OuterGenericType(field.ContainingType, out HashSet<ITypeSymbol> parameters);
                    if (generic == null || UsesAny(field.Type, parameters)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), field.Name, generic.Name));
                }
            }), SyntaxKind.FieldDeclaration);
        }

        // The innermost generic type around the field, and every type parameter in scope.
        private static INamedTypeSymbol OuterGenericType(INamedTypeSymbol type, out HashSet<ITypeSymbol> parameters)
        {
            parameters = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            INamedTypeSymbol generic = null;
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                if (current.TypeParameters.Length == 0) continue;
                if (generic == null) generic = current;
                foreach (ITypeParameterSymbol parameter in current.TypeParameters) parameters.Add(parameter);
            }
            return generic;
        }

        private static bool UsesAny(ITypeSymbol type, HashSet<ITypeSymbol> parameters)
        {
            switch (type)
            {
                case ITypeParameterSymbol parameter: return parameters.Contains(parameter);
                case IArrayTypeSymbol array: return UsesAny(array.ElementType, parameters);
                case IPointerTypeSymbol pointer: return UsesAny(pointer.PointedAtType, parameters);
                case INamedTypeSymbol named:
                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        if (UsesAny(argument, parameters)) return true;
                    }
                    return named.ContainingType != null && UsesAny(named.ContainingType, parameters);
                default:
                    return false;
            }
        }
    }
}