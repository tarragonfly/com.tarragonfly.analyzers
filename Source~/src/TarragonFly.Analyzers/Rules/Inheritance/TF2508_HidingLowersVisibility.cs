using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2508: a method or property that hides a public base member with the same signature, without
    // "new", and is itself less visible.
    //
    // It doesn't make the member any less public: callers holding the base type, or code outside the
    // class, still reach the base version, so the "hidden" behaviour runs only sometimes.
    //
    // Members marked "new" were hidden on purpose and are fine, as are overloads with other parameters.
    // Reported on the member name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HidingLowersVisibilityAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2508",
            "Hiding member is less visible than the base member",
            "'{0}' hides a public member of '{1}' but is less visible, so other callers still reach the base one",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MemberDeclarationSyntax declaration = (MemberDeclarationSyntax)context.Node;
                foreach (SyntaxToken modifier in declaration.Modifiers)
                {
                    if (modifier.IsKind(SyntaxKind.NewKeyword) || modifier.IsKind(SyntaxKind.OverrideKeyword)) return;
                }
                ISymbol member = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
                if (member == null || member.DeclaredAccessibility == Accessibility.Public || member.ContainingType?.TypeKind != TypeKind.Class) return;

                for (INamedTypeSymbol type = member.ContainingType.BaseType; type != null; type = type.BaseType)
                {
                    foreach (ISymbol candidate in type.GetMembers(member.Name))
                    {
                        if (candidate.DeclaredAccessibility != Accessibility.Public || !SameSignature(member, candidate)) continue;
                        SyntaxToken name = declaration is MethodDeclarationSyntax method ? method.Identifier : ((PropertyDeclarationSyntax)declaration).Identifier;
                        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), member.Name, type.Name));
                        return;
                    }
                }
            }), SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration);
        }

        private static bool SameSignature(ISymbol member, ISymbol candidate)
        {
            if (member is IPropertySymbol) return candidate is IPropertySymbol;
            if (!(member is IMethodSymbol method) || !(candidate is IMethodSymbol other)) return false;
            if (method.Parameters.Length != other.Parameters.Length || method.Arity != other.Arity) return false;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                if (method.Parameters[i].RefKind != other.Parameters[i].RefKind || !SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, other.Parameters[i].Type)) return false;
            }
            return true;
        }
    }
}