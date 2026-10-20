using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

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
    // Union, Intersect, Except and SequenceEqual, also called as "Enumerable.Union(enemies, enemies)",
    // with a comparer, through ?., with ! or with the collection in parentheses. Methods with these
    // names on other types count too. AddRange and Concat with themselves duplicate the items on purpose
    // and are fine. Reported on the collection.
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
            start.RegisterOperationAction(context =>
            {
                IInvocationOperation invocation = (IInvocationOperation)context.Operation;
                IMethodSymbol method = invocation.TargetMethod;
                if (!Methods.Contains(method.Name) || !scope.IsAnalyzed(invocation.Syntax.SyntaxTree)) return;

                // An extension method gets the collection as its first parameter, written before the dot or not.
                IOperation collection = method.IsExtensionMethod ? Argument(invocation, 0) : invocation.Instance;
                IOperation other = Argument(invocation, method.IsExtensionMethod ? 1 : 0);
                if (collection == null || other == null) return;
                SyntaxNode collectionSyntax = WithoutParentheses(collection.Syntax);
                if (!SyntaxFactory.AreEquivalent(collectionSyntax, WithoutParentheses(other.Syntax))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, collectionSyntax.GetLocation(), collectionSyntax.ToString(), method.Name));
            }, OperationKind.Invocation);
        }

        // The value passed for the parameter at this position, wherever the argument is written.
        private static IOperation Argument(IInvocationOperation invocation, int ordinal)
        {
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                if (argument.Parameter?.Ordinal == ordinal) return argument.Value;
            }
            return null;
        }

        private static SyntaxNode WithoutParentheses(SyntaxNode node)
        {
            while (node is ParenthesizedExpressionSyntax parenthesized) node = parenthesized.Expression;
            return node;
        }
    }
}