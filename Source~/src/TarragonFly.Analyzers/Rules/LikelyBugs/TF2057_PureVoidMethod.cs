using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2057: [Pure] on a method that returns nothing and has no out or ref parameters.
    //
    // [Pure] promises no side effects, so a call to it can only matter through its result; with no
    // result, either the attribute is wrong or the method is.
    //
    // Reported on the attribute.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PureVoidMethodAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2057",
            "[Pure] method returning void",
            "A [Pure] method that returns nothing can't do anything; remove [Pure] or return the result",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method) || !method.ReturnsVoid) return;
                foreach (IParameterSymbol parameter in method.Parameters)
                {
                    if (parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref) return;
                }

                foreach (AttributeListSyntax list in declaration.AttributeLists)
                {
                    foreach (AttributeSyntax attribute in list.Attributes)
                    {
                        if (!(context.SemanticModel.GetTypeInfo(attribute, context.CancellationToken).Type is INamedTypeSymbol type)) continue;
                        if (SymbolNames.IsNamed(type, "System.Diagnostics.Contracts.PureAttribute")) context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.Name.GetLocation()));
                    }
                }
            }), SyntaxKind.MethodDeclaration);
        }
    }
}