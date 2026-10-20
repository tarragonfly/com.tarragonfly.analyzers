using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1005: a parameter of an override, interface implementation or partial method body with a
    // different name from the declaration it comes from.
    //
    // Named arguments then depend on the static type: TakeDamage(amount: 5) compiles against the
    // interface and fails against the class, and swapped names silently swap values.
    //
    // Explicit interface implementations are checked too. A parameter may be renamed when the
    // implementation puts a specific type where the base declaration has a type parameter, since the
    // base could only give it a generic name: "Init(IUIViewManager viewManager)" for Init(args)'
    // Init(TFirstArgument firstArgument), Compare(Enemy left, Enemy right) for
    // IComparer<T>.Compare(x, y), or List<Enemy> for List<T>. Keeping a type parameter, as in
    // "InitTarget(TWrapper wrapper)" for InitTarget(T target), means keeping the name too, and arrays
    // such as int[] for T[] don't count. A method that implements members of several interfaces is
    // checked against each of them, so a name that matches one but not another is still reported.
    // Reported on each renamed parameter.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RenamedParameterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1005",
            "Parameter renamed from the base declaration",
            "Rename '{0}' to '{1}' to match the declaration it overrides or implements",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;

                List<IMethodSymbol> bases = BaseMembers.Of(method);
                if (method.PartialDefinitionPart != null) bases.Add(method.PartialDefinitionPart);

                SeparatedSyntaxList<ParameterSyntax> parameters = declaration.ParameterList.Parameters;
                for (int i = 0; i < parameters.Count && i < method.Parameters.Length; i++)
                {
                    string name = method.Parameters[i].Name;
                    foreach (IMethodSymbol baseMethod in bases)
                    {
                        if (i >= baseMethod.Parameters.Length || baseMethod.Parameters[i].Name == name) continue;
                        if (Specializes(baseMethod.OriginalDefinition.Parameters[i].Type, method.Parameters[i].Type)) continue;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, parameters[i].Identifier.GetLocation(), name, baseMethod.Parameters[i].Name));
                        break;
                    }
                }
            }), SyntaxKind.MethodDeclaration);
        }

        // The implementation puts a specific type where the base declaration has a type parameter,
        // directly (int for T) or as a type argument (List<Enemy> for List<T>).
        private static bool Specializes(ITypeSymbol declared, ITypeSymbol implemented)
        {
            if (declared is ITypeParameterSymbol) return !(implemented is ITypeParameterSymbol);
            if (!(declared is INamedTypeSymbol declaredType) || !(implemented is INamedTypeSymbol implementedType)) return false;
            if (declaredType.TypeArguments.Length != implementedType.TypeArguments.Length) return false;
            for (int i = 0; i < declaredType.TypeArguments.Length; i++)
            {
                if (Specializes(declaredType.TypeArguments[i], implementedType.TypeArguments[i])) return true;
            }
            return false;
        }
    }
}