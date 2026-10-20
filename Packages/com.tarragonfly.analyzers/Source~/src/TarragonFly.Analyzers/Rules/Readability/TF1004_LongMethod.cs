using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1004: a method, constructor, destructor or operator whose body has more lines of code than
    // max_method_lines (default 80).
    //
    // A long Update or Init method is hard to follow and hard to change without breaking another
    // part of it. Pull named steps out into methods.
    //
    // Counts lines inside the braces (or the expression of an expression body) that hold code; blank
    // and comment-only lines are free. Lambdas and local functions count toward the method they sit in
    // and aren't reported on their own; accessors aren't checked. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LongMethodAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1004",
            "Method is too long",
            "This method has {0} lines of code, more than the limit of {1}; pull named steps out into methods",
            "Readability",
            CodeKind.Game,
            description: "Counts lines of code in the body. The limit is max_method_lines in the settings file.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxMethodLines;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BaseMethodDeclarationSyntax method = (BaseMethodDeclarationSyntax)context.Node;
                IEnumerable<SyntaxToken> tokens = BodyTokens(method);
                if (tokens == null) return;
                int lines = CodeLines.Count(tokens, method.SyntaxTree.GetText(context.CancellationToken));
                if (lines > limit) context.ReportDiagnostic(Diagnostic.Create(Rule, Name(method).GetLocation(), lines, limit));
            }), SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration, SyntaxKind.DestructorDeclaration,
                SyntaxKind.OperatorDeclaration, SyntaxKind.ConversionOperatorDeclaration);
        }

        private static IEnumerable<SyntaxToken> BodyTokens(BaseMethodDeclarationSyntax method)
        {
            if (method.Body != null) return InsideBraces(method.Body);
            return method.ExpressionBody?.Expression.DescendantTokens();
        }

        private static IEnumerable<SyntaxToken> InsideBraces(BlockSyntax body)
        {
            foreach (SyntaxToken token in body.DescendantTokens())
            {
                if (token != body.OpenBraceToken && token != body.CloseBraceToken) yield return token;
            }
        }

        private static SyntaxToken Name(BaseMethodDeclarationSyntax method)
        {
            switch (method)
            {
                case MethodDeclarationSyntax m: return m.Identifier;
                case ConstructorDeclarationSyntax c: return c.Identifier;
                case DestructorDeclarationSyntax d: return d.Identifier;
                case OperatorDeclarationSyntax o: return o.OperatorToken;
                default: return ((ConversionOperatorDeclarationSyntax)method).OperatorKeyword;
            }
        }
    }
}