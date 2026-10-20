using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2508: a method, property or indexer that hides a public base member with the same signature,
    // without "new", and is itself less visible.
    //
    // It doesn't make the member any less public: callers holding the base type, or code outside the
    // class, still reach the base version, so the "hidden" behaviour runs only sometimes.
    //
    // Members marked "new" were hidden on purpose and are fine, as are overloads with other parameters.
    // Generic methods match by the position of their type parameters, and tuple element names are
    // ignored, as the compiler does. Sealed classes count too. Reported on the member name, or on
    // "this" for an indexer.
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
                        context.ReportDiagnostic(Diagnostic.Create(Rule, Name(declaration).GetLocation(), member.Name, type.Name));
                        return;
                    }
                }
            }), SyntaxKind.MethodDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration);
        }

        private static SyntaxToken Name(MemberDeclarationSyntax declaration)
        {
            switch (declaration)
            {
                case MethodDeclarationSyntax method: return method.Identifier;
                case PropertyDeclarationSyntax property: return property.Identifier;
                default: return ((IndexerDeclarationSyntax)declaration).ThisKeyword;
            }
        }

        private static bool SameSignature(ISymbol member, ISymbol candidate)
        {
            if (member is IPropertySymbol property)
            {
                return candidate is IPropertySymbol other && SameParameters(property.Parameters, other.Parameters);
            }
            if (!(member is IMethodSymbol method) || !(candidate is IMethodSymbol baseMethod) || method.Arity != baseMethod.Arity) return false;
            if (method.Arity > 0) baseMethod = baseMethod.Construct(method.TypeParameters.Cast<ITypeSymbol>().ToArray());
            return SameParameters(method.Parameters, baseMethod.Parameters);
        }

        private static bool SameParameters(ImmutableArray<IParameterSymbol> mine, ImmutableArray<IParameterSymbol> theirs)
        {
            if (mine.Length != theirs.Length) return false;
            for (int i = 0; i < mine.Length; i++)
            {
                if (mine[i].RefKind != theirs[i].RefKind || !SymbolEqualityComparer.Default.Equals(WithoutTupleNames(mine[i].Type), WithoutTupleNames(theirs[i].Type))) return false;
            }
            return true;
        }

        private static ITypeSymbol WithoutTupleNames(ITypeSymbol type)
        {
            return type is INamedTypeSymbol tuple && tuple.IsTupleType ? tuple.TupleUnderlyingType ?? tuple : type;
        }
    }
}