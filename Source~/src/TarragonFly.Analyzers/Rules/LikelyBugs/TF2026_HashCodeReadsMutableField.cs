using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2026: a class's GetHashCode() that reads a field which isn't readonly.
    //
    // Change that field while the object sits in a Dictionary key or HashSet and lookups stop finding
    // it, because it's now filed under the old hash.
    //
    // Only instance fields count; static, const and readonly ones are fine, as are properties. Structs
    // and records aren't checked. Reported once, on the GetHashCode name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HashCodeReadsMutableFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2026",
            "GetHashCode reads a mutable field",
            "GetHashCode reads '{0}', which can change; dictionary and set lookups break once it does",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
                if (method.Identifier.ValueText != "GetHashCode" || method.ParameterList.Parameters.Count != 0) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is IMethodSymbol symbol) || !symbol.IsOverride) return;
                INamedTypeSymbol type = symbol.ContainingType;
                if (type.TypeKind != TypeKind.Class || type.IsRecord) return;

                foreach (SyntaxNode node in method.DescendantNodes())
                {
                    if (!(node is IdentifierNameSyntax name)) continue;
                    if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is IFieldSymbol field
                        && !field.IsStatic && !field.IsConst && !field.IsReadOnly && !field.IsImplicitlyDeclared)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), field.Name));
                        return;
                    }
                }
            }), SyntaxKind.MethodDeclaration);
        }
    }
}