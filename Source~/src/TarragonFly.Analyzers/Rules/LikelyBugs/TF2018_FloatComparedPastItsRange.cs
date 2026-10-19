using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2018: a float compared with a double constant outside the float range, such as "distance >
    // double.MaxValue", which is always false.
    //
    // Usually float.MaxValue or float.PositiveInfinity was meant.
    //
    // Integer comparisons that can never be true, such as "aByte > 300", are left to the C# compiler,
    // which warns about them (CS0652). Reported on the whole comparison. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FloatComparedPastItsRangeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2018",
            "Comparison that is always true or false",
            "A float never reaches this value, so the comparison always gives the same result; was float.MaxValue meant?",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BinaryExpressionSyntax comparison = (BinaryExpressionSyntax)context.Node;
                if (IsFloat(context.SemanticModel, comparison.Left) && IsBeyondFloat(context.SemanticModel, comparison.Right)
                    || IsFloat(context.SemanticModel, comparison.Right) && IsBeyondFloat(context.SemanticModel, comparison.Left))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, comparison.GetLocation()));
                }
            }), SyntaxKind.LessThanExpression, SyntaxKind.LessThanOrEqualExpression, SyntaxKind.GreaterThanExpression,
                SyntaxKind.GreaterThanOrEqualExpression, SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
        }

        private static bool IsFloat(SemanticModel model, ExpressionSyntax expression)
        {
            return model.GetTypeInfo(expression).Type?.SpecialType == SpecialType.System_Single;
        }

        private static bool IsBeyondFloat(SemanticModel model, ExpressionSyntax expression)
        {
            Optional<object> value = model.GetConstantValue(expression);
            return value.HasValue && value.Value is double number && !double.IsInfinity(number) && Math.Abs(number) > float.MaxValue;
        }
    }
}