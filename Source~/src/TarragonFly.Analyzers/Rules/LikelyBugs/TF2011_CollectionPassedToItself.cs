using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2011: a set or LINQ set operation given the collection it runs on, as in
    // "visited.UnionWith(visited)" or "enemies.Except(enemies)".
    //
    // The result is fixed (unchanged, empty or always true), so another collection was almost
    // certainly meant.
    //
    // Checked: the ISet methods UnionWith, IntersectWith, ExceptWith, SymmetricExceptWith, IsSubsetOf,
    // IsSupersetOf, IsProperSubsetOf, IsProperSupersetOf, Overlaps and SetEquals, and the LINQ methods
    // Union, Intersect, Except and SequenceEqual. AddRange and Concat with themselves duplicate the
    // items on purpose and are fine. Reported on the collection.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CollectionPassedToItselfAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2011",
            "Collection passed to its own method",
            "'{0}' is passed to its own {1}, which gives a fixed result; another collection was probably meant",
            "LikelyBugs",
            CodeKind.All);

        private static readonly HashSet<string> Methods = new HashSet<string>
        {
            "UnionWith", "IntersectWith", "ExceptWith", "SymmetricExceptWith", "IsSubsetOf", "IsSupersetOf",
            "IsProperSubsetOf", "IsProperSupersetOf", "Overlaps", "SetEquals", "Union", "Intersect", "Except", "SequenceEqual"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access) || !Methods.Contains(access.Name.Identifier.ValueText)) return;
                if (invocation.ArgumentList.Arguments.Count != 1 || !SyntaxFactory.AreEquivalent(access.Expression, invocation.ArgumentList.Arguments[0].Expression)) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Expression.GetLocation(), access.Expression.ToString(), access.Name.Identifier.ValueText));
            }), SyntaxKind.InvocationExpression);
        }
    }
}