using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1017: a field, property, method or event of a nested type with the same name as a static member
    // of a type it sits in, or a type nested two or more levels down with the same name as a type
    // nested in an outer one, as in a second "Styles" class.
    //
    // Inside the nested type, "Reset()", "Count" or "Styles" then means its own member, while readers of
    // the outer code expect the outer one.
    //
    // Any outer level counts, overloads with other parameters and types with other type parameters
    // included, but not members inherited from base classes. Interfaces nested in a type are checked;
    // enum members, constructors and operators aren't, and a type and a member with the same name don't
    // count. Reported on the nested member's or type's name.
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

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is INamedTypeSymbol type)) return;
                SyntaxToken name = context.Node is DelegateDeclarationSyntax delegateDeclaration ? delegateDeclaration.Identifier : ((BaseTypeDeclarationSyntax)context.Node).Identifier;
                for (INamedTypeSymbol outer = type.ContainingType?.ContainingType; outer != null; outer = outer.ContainingType)
                {
                    if (outer.GetTypeMembers(type.Name).IsEmpty) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), type.Name, outer.Name));
                    return;
                }
            }), SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.EnumDeclaration,
                SyntaxKind.DelegateDeclaration, SyntaxKind.RecordDeclaration, SyntaxKind.RecordStructDeclaration);
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