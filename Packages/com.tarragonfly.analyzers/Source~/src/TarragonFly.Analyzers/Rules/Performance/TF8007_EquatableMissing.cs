using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8007: a type with a public "bool Equals(T other)" for its own type that doesn't declare
    // IEquatable<T>.
    //
    // Dictionary, HashSet, List.Contains and LINQ only use Equals(T) through IEquatable<T>; without
    // it they call Equals(object) instead, which boxes every struct comparison and can disagree with
    // the typed version.
    //
    // Static Equals methods aren't checked. Reported on the type name.
    //
    // Structs are checked too, since boxing is where this hurts most.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EquatableMissingAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8007",
            "Equals(T) without IEquatable<T>",
            "'{0}' has Equals({0}) but doesn't implement IEquatable<{0}>, so collections call Equals(object) and box structs instead",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol type) || type.IsRecord) return;
                if (SymbolNames.Implements(type, i => SymbolNames.IsNamed(i.OriginalDefinition, "System.IEquatable`1") && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type))) return;

                foreach (MemberDeclarationSyntax member in declaration.Members)
                {
                    if (!(member is MethodDeclarationSyntax method) || method.Identifier.ValueText != "Equals") continue;
                    if (!(context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is IMethodSymbol symbol)) continue;
                    if (symbol.IsStatic || symbol.DeclaredAccessibility != Accessibility.Public || symbol.ReturnType.SpecialType != SpecialType.System_Boolean) continue;
                    if (symbol.Parameters.Length != 1 || !SymbolEqualityComparer.Default.Equals(symbol.Parameters[0].Type, type)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), type.Name));
                    return;
                }
            }), SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration);
        }
    }
}