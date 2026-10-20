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
    // Static and instance overloads, constructors, interface members and their implementations all
    // count. Reported on the first parameter the other overload lacks; later defaults stay reachable
    // through calls that pass it.
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
                if (required == method.Parameters.Length) return;

                HashSet<int> reported = new HashSet<int>();
                foreach (ISymbol member in method.ContainingType.GetMembers(method.Name))
                {
                    if (!(member is IMethodSymbol other) || SymbolEqualityComparer.Default.Equals(other, method) || other.IsImplicitlyDeclared) continue;
                    int hidden = other.Parameters.Length;
                    if (hidden >= method.Parameters.Length || !method.Parameters[hidden].IsOptional) continue;
                    if (RequiredCount(other) > required || !MatchesFirstParameters(method, other)) continue;
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

        // Whether all of the other overload's parameters match this one's first parameters.
        private static bool MatchesFirstParameters(IMethodSymbol method, IMethodSymbol other)
        {
            if (other.Arity != method.Arity) return false;
            if (method.Arity > 0) other = other.Construct(method.TypeParameters.Cast<ITypeSymbol>().ToArray());
            for (int i = 0; i < other.Parameters.Length; i++)
            {
                IParameterSymbol mine = method.Parameters[i];
                IParameterSymbol theirs = other.Parameters[i];
                if (mine.RefKind != theirs.RefKind || !SymbolEqualityComparer.Default.Equals(mine.Type, theirs.Type)) return false;
            }
            return true;
        }
    }
}