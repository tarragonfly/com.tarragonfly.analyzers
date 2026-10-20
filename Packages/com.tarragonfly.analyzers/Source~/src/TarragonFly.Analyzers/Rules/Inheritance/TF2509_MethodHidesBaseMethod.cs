using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2509: a method with the same name and parameter count as a base class method, whose parameter
    // types are the same or base types of the base method's, at least one strictly.
    //
    // Feed(Animal) in a derived class takes every call written as Feed(dog) on the derived type, so
    // the base Feed(Dog) is accidentally unreachable there, without any "hides" warning from the
    // compiler.
    //
    // Only class relationships count between parameter types; a numeric widening like int to long
    // doesn't. Generic methods count when both have the same number of type parameters, matched by
    // position. Private base methods aren't checked. Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MethodHidesBaseMethodAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2509",
            "Method accidentally hides a base method",
            "'{0}' takes every call meant for '{1}.{0}', which has more specific parameters; rename one of them",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method) || method.IsOverride || method.Parameters.Length == 0) return;

                for (INamedTypeSymbol type = method.ContainingType.BaseType; type != null; type = type.BaseType)
                {
                    foreach (ISymbol candidate in type.GetMembers(method.Name))
                    {
                        if (!(candidate is IMethodSymbol baseMethod) || baseMethod.DeclaredAccessibility == Accessibility.Private || !TakesItsCalls(method, baseMethod)) continue;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), method.Name, type.Name));
                        return;
                    }
                }
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool TakesItsCalls(IMethodSymbol method, IMethodSymbol baseMethod)
        {
            if (method.Parameters.Length != baseMethod.Parameters.Length || method.Arity != baseMethod.Arity) return false;
            if (method.Arity > 0) baseMethod = baseMethod.Construct(method.TypeParameters.Cast<ITypeSymbol>().ToArray());
            bool wider = false;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                ITypeSymbol mine = method.Parameters[i].Type;
                ITypeSymbol theirs = baseMethod.Parameters[i].Type;
                if (SymbolEqualityComparer.Default.Equals(mine, theirs)) continue;
                if (!IsBaseClassOf(mine, theirs)) return false;
                wider = true;
            }
            return wider;
        }

        private static bool IsBaseClassOf(ITypeSymbol baseType, ITypeSymbol type)
        {
            for (ITypeSymbol current = type.BaseType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
            }
            return false;
        }
    }
}