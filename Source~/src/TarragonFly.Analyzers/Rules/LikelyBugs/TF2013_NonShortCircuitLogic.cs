using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2013: & or | between two bool values, where && or || was meant.
    //
    // Both sides always run, so "target != null & target.IsAlive" still throws on a null target, and
    // expensive checks run when the first side already decided the result.
    //
    // ^ and the compound &= and |= are fine. Reported on the operator. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NonShortCircuitLogicAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2013",
            "& or | where && or || was meant",
            "Use '{0}{0}' instead of '{0}' so the right side only runs when it's needed",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax binary = (BinaryExpressionSyntax)context.Node;
                if (IsBool(context.SemanticModel, binary.Left) && IsBool(context.SemanticModel, binary.Right))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, binary.OperatorToken.GetLocation(), binary.OperatorToken.Text));
                }
            }), SyntaxKind.BitwiseAndExpression, SyntaxKind.BitwiseOrExpression);
        }

        private static bool IsBool(SemanticModel model, ExpressionSyntax expression)
        {
            return model.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_Boolean;
        }
    }
}