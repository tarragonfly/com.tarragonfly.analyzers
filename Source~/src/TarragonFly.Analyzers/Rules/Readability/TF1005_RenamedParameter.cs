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
    // Explicit interface implementations are checked too. A parameter whose type in the base
    // declaration is a type parameter may be renamed, since the base could only give it a generic name:
    // "Init(IUIViewManager viewManager)" for Init(args)' Init(TFirstArgument firstArgument), or
    // Compare(left, right) for IComparer<T>.Compare(x, y). Reported on each renamed parameter.
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
                        if (baseMethod.OriginalDefinition.Parameters[i].Type is ITypeParameterSymbol) continue;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, parameters[i].Identifier.GetLocation(), name, baseMethod.Parameters[i].Name));
                        break;
                    }
                }
            }), SyntaxKind.MethodDeclaration);
        }
    }
}