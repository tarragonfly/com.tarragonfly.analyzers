using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2071: "scoped" as a type or parameter name in a parenthesized lambda parameter list, as in
    // "(scoped value) => ...", "(scoped[] values) => ..." or "(scoped) => ...". From C# 14, "scoped"
    // there is a modifier, so the lambda changes meaning when Unity moves to a newer C# version.
    //
    // "@scoped", simple lambdas without parentheses, anonymous methods and ordinary methods are fine.
    // Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ScopedLambdaParameterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2071",
            "'scoped' in lambda parameters",
            "From C# 14, 'scoped' in a lambda parameter list is a modifier; write '@scoped'",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ParenthesizedLambdaExpressionSyntax lambda = (ParenthesizedLambdaExpressionSyntax)context.Node;
                foreach (ParameterSyntax parameter in lambda.ParameterList.Parameters)
                {
                    SyntaxToken token = parameter.Type == null ? parameter.Identifier : StartsLikeAModifier(parameter.Type) ? parameter.Type.GetFirstToken() : default;
                    if (token.IsKind(SyntaxKind.IdentifierToken) && token.Text == "scoped") context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation()));
                }
            }), SyntaxKind.ParenthesizedLambdaExpression);
        }

        // A type written "scoped", "scoped[]", "scoped<int>" or "scoped?", but not "scoped.Inner".
        private static bool StartsLikeAModifier(TypeSyntax type)
        {
            switch (type)
            {
                case ArrayTypeSyntax array: return StartsLikeAModifier(array.ElementType);
                case NullableTypeSyntax nullable: return StartsLikeAModifier(nullable.ElementType);
                case SimpleNameSyntax _: return true;
                default: return false;
            }
        }
    }
}