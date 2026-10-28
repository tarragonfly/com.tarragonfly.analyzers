using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2500: an override or interface implementation whose default parameter values differ from the
    // declaration it comes from.
    //
    // C# picks the default from the type the call is written against, so enemy.Move() uses one speed
    // through a Goblin variable and another through an Enemy variable.
    //
    // Reported: a different default (on the value), an added default (on "= value") and a dropped
    // default (on the parameter name). In an explicit interface implementation any default is reported,
    // since callers can only reach it through the interface.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ChangedDefaultParameterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2500",
            "Default parameter value differs from the base declaration",
            "The default of '{0}' differs from the declaration this overrides or implements, so the value depends on the variable's type",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                SeparatedSyntaxList<ParameterSyntax> parameters = declaration.ParameterList.Parameters;

                if (declaration.ExplicitInterfaceSpecifier != null)
                {
                    foreach (ParameterSyntax parameter in parameters)
                    {
                        if (context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is IParameterSymbol symbol && symbol.HasExplicitDefaultValue)
                            context.ReportDiagnostic(Diagnostic.Create(Rule, parameter.Default?.GetLocation() ?? parameter.Identifier.GetLocation(), parameter.Identifier.ValueText));
                    }
                    return;
                }

                List<IMethodSymbol> bases = BaseMembers.Of(method);
                for (int i = 0; i < parameters.Count && i < method.Parameters.Length; i++)
                {
                    Location location = Mismatch(parameters[i], method.Parameters[i], bases, i);
                    if (location != null) context.ReportDiagnostic(Diagnostic.Create(Rule, location, parameters[i].Identifier.ValueText));
                }
            }), SyntaxKind.MethodDeclaration);
        }

        private static Location Mismatch(ParameterSyntax syntax, IParameterSymbol parameter, List<IMethodSymbol> bases, int index)
        {
            foreach (IMethodSymbol baseMethod in bases)
            {
                if (index >= baseMethod.Parameters.Length) continue;
                IParameterSymbol baseParameter = baseMethod.Parameters[index];
                if (baseParameter.HasExplicitDefaultValue && parameter.HasExplicitDefaultValue)
                {
                    if (!Equals(baseParameter.ExplicitDefaultValue, parameter.ExplicitDefaultValue)) return syntax.Default?.Value.GetLocation() ?? syntax.Identifier.GetLocation();
                }
                else if (parameter.HasExplicitDefaultValue)
                {
                    return syntax.Default?.GetLocation() ?? syntax.Identifier.GetLocation();
                }
                else if (baseParameter.HasExplicitDefaultValue)
                {
                    return syntax.Identifier.GetLocation();
                }
            }
            return null;
        }
    }
}
