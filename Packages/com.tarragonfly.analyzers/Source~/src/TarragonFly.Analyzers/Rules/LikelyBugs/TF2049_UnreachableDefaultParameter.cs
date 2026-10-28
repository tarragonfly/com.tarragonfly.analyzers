using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2049: an optional parameter whose default can never be used, because another overload takes
    // exactly the parameters before it.
    //
    // With Play(string clip) and Play(string clip, float volume = 1f), Play("hit") always calls the
    // first, so the volume default is dead and readers can't tell which overload runs.
    //
    // The other overload's parameters must match this one's first parameters in type and ref kind
    // (method type parameters by position), and it must need no more arguments than this one does:
    // then every call leaving out the next parameter goes to the other overload or doesn't compile.
    // Static and instance overloads, constructors and interface members all count. An override or
    // an interface implementation is skipped: TF2500 makes it repeat the base declaration's
    // defaults, so the fix belongs where the pair is declared, and a fake implementing the
    // interface would otherwise be stuck between the two rules. A default with another optional
    // parameter after it isn't reported: Play("hit", fade: 0.5f) skips to the later one by name and
    // so does use it. Reported on the first parameter the other overload lacks.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnreachableDefaultParameterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2049",
            "Default value hidden by another overload",
            "This default is never used: calls leaving it out go to the overload of '{0}' without it",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BaseMethodDeclarationSyntax declaration = (BaseMethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;

                int required = RequiredCount(method);
                if (required == method.Parameters.Length || BaseMembers.IsOverrideOrImplementation(method)) return;

                HashSet<int> reported = new HashSet<int>();
                foreach (ISymbol member in method.ContainingType.GetMembers(method.Name))
                {
                    if (!(member is IMethodSymbol other) || SymbolEqualityComparer.Default.Equals(other, method) || other.IsImplicitlyDeclared) continue;
                    if (!VisibleWherever(other.DeclaredAccessibility, method.DeclaredAccessibility) || other.IsStatic != method.IsStatic) continue;
                    int hidden = other.Parameters.Length;
                    if (hidden >= method.Parameters.Length || !method.Parameters[hidden].IsOptional) continue;
                    if (RequiredCount(other) > required || !MatchesFirstParameters(method, other) || HasOptionalAfter(method, hidden)) continue;
                    if (reported.Add(hidden))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.ParameterList.Parameters[hidden].GetLocation(), method.Name));
                    }
                }
            }), SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration);
        }

        private static int RequiredCount(IMethodSymbol method)
        {
            int required = 0;
            while (required < method.Parameters.Length && !method.Parameters[required].IsOptional && !method.Parameters[required].IsParams) required++;
            return required;
        }

        // A named argument can skip past the parameter to a later optional one, which uses its default.
        private static bool HasOptionalAfter(IMethodSymbol method, int index)
        {
            for (int i = index + 1; i < method.Parameters.Length; i++)
            {
                if (method.Parameters[i].IsOptional) return true;
            }
            return false;
        }

        // Whether all of the other overload's parameters match this one's first parameters.
        // Whether code that can call the method with the default can also call the other overload,
        // so the shorter call always goes to the other one.
        private static bool VisibleWherever(Accessibility other, Accessibility method)
        {
            if (other == method || other == Accessibility.Public) return true;
            switch (other)
            {
                case Accessibility.ProtectedOrInternal:
                    return method != Accessibility.Public;
                case Accessibility.Protected:
                case Accessibility.Internal:
                    return method == Accessibility.ProtectedAndInternal || method == Accessibility.Private;
                case Accessibility.ProtectedAndInternal:
                    return method == Accessibility.Private;
                default:
                    return false;
            }
        }

        private static bool MatchesFirstParameters(IMethodSymbol method, IMethodSymbol other)
        {
            if (other.Arity != method.Arity) return false;
            for (int i = 0; i < method.Arity; i++)
            {
                ITypeParameterSymbol mine = method.TypeParameters[i];
                ITypeParameterSymbol theirs = other.TypeParameters[i];
                if (mine.HasConstructorConstraint != theirs.HasConstructorConstraint || mine.HasReferenceTypeConstraint != theirs.HasReferenceTypeConstraint
                    || mine.HasValueTypeConstraint != theirs.HasValueTypeConstraint || mine.HasUnmanagedTypeConstraint != theirs.HasUnmanagedTypeConstraint
                    || mine.HasNotNullConstraint != theirs.HasNotNullConstraint || !mine.ConstraintTypes.Select(type => type.ToDisplayString()).SequenceEqual(theirs.ConstraintTypes.Select(type => type.ToDisplayString()))) return false;
            }
            if (method.Arity > 0) other = other.Construct(method.TypeParameters.Cast<ITypeSymbol>().ToArray());
            for (int i = 0; i < other.Parameters.Length; i++)
            {
                IParameterSymbol mine = method.Parameters[i];
                IParameterSymbol theirs = other.Parameters[i];
                if (mine.Name != theirs.Name || mine.RefKind != theirs.RefKind || !SymbolEqualityComparer.Default.Equals(mine.Type, theirs.Type)) return false;
            }
            return true;
        }
    }
}
