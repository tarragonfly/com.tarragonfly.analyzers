using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2049: optional parameters whose default can never be used, because another overload takes
    // exactly the remaining required parameters.
    //
    // With Play(string clip) and Play(string clip, float volume = 1f), Play("hit") always calls the
    // first, so the volume default is dead and readers can't tell which overload runs.
    //
    // The other overload must match the required parameters' types and ref kinds exactly; static and
    // instance overloads both count. Reported on each optional parameter.
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
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;

                int required = 0;
                while (required < method.Parameters.Length && !method.Parameters[required].IsOptional) required++;
                if (required == method.Parameters.Length) return;

                foreach (ISymbol member in method.ContainingType.GetMembers(method.Name))
                {
                    if (!(member is IMethodSymbol other) || SymbolEqualityComparer.Default.Equals(other, method) || !MatchesRequired(method, other, required)) continue;
                    for (int i = required; i < declaration.ParameterList.Parameters.Count; i++)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.ParameterList.Parameters[i].GetLocation(), method.Name));
                    }
                    return;
                }
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool MatchesRequired(IMethodSymbol method, IMethodSymbol other, int required)
        {
            if (other.Parameters.Length != required || other.Arity != method.Arity) return false;
            for (int i = 0; i < required; i++)
            {
                IParameterSymbol mine = method.Parameters[i];
                IParameterSymbol theirs = other.Parameters[i];
                if (mine.RefKind != theirs.RefKind || !SymbolEqualityComparer.Default.Equals(mine.Type, theirs.Type)) return false;
            }
            return true;
        }
    }
}