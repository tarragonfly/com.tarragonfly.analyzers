using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1017: a field, property, method or event of a nested type with the same name as a static member
    // of a type it sits in.
    //
    // Inside the nested type, "Reset()" or "Count" then means its own member, while readers of the
    // outer code expect the outer static one.
    //
    // Any outer level counts, overloads with other parameters included. Interfaces nested in a type are
    // checked; enum members, constructors and operators aren't. Reported on the nested member's name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NestedMemberHidesOuterStaticAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1017",
            "Nested type member hides an outer static member",
            "'{0}' hides the static member of '{1}' with the same name; rename it",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxToken name = Name(context.Node);
                if (name.IsKind(SyntaxKind.None)) return;
                INamedTypeSymbol nested = context.SemanticModel.GetDeclaredSymbol(context.Node.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>(), context.CancellationToken) as INamedTypeSymbol;
                if (nested == null) return;

                for (INamedTypeSymbol outer = nested.ContainingType; outer != null; outer = outer.ContainingType)
                {
                    foreach (ISymbol member in outer.GetMembers(name.ValueText))
                    {
                        if (!member.IsStatic || member.IsImplicitlyDeclared || !IsCheckedKind(member)) continue;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), name.ValueText, outer.Name));
                        return;
                    }
                }
            }), SyntaxKind.VariableDeclarator, SyntaxKind.PropertyDeclaration, SyntaxKind.MethodDeclaration, SyntaxKind.EventDeclaration);
        }

        private static SyntaxToken Name(SyntaxNode node)
        {
            switch (node)
            {
                case VariableDeclaratorSyntax declarator when declarator.Parent?.Parent is BaseFieldDeclarationSyntax: return declarator.Identifier;
                case PropertyDeclarationSyntax property: return property.Identifier;
                case MethodDeclarationSyntax method: return method.Identifier;
                case EventDeclarationSyntax eventDeclaration: return eventDeclaration.Identifier;
                default: return default;
            }
        }

        private static bool IsCheckedKind(ISymbol member)
        {
            return member is IFieldSymbol || member is IPropertySymbol || member is IEventSymbol
                || (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary);
        }
    }
}