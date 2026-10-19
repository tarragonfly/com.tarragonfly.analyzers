using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2023: a for loop whose condition is already false before the first pass, as in "for (int i =
    // 10; i < 5; i++)" or "for (...; false; ...)".
    //
    // The body never runs, usually because the start and end values or the comparison got swapped.
    //
    // The counter must start at a constant, from its declaration or an assignment in the initializer,
    // and the condition must compare it with a constant. Reported on the condition.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ForLoopNeverRunsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2023",
            "For loop that never runs",
            "This condition is false from the start, so the loop body never runs",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ForStatementSyntax loop = (ForStatementSyntax)context.Node;
                if (loop.Condition == null) return;
                if (loop.Condition.IsKind(SyntaxKind.FalseLiteralExpression) || StartsFalse(context.SemanticModel, loop))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, loop.Condition.GetLocation()));
                }
            }), SyntaxKind.ForStatement);
        }

        private static bool StartsFalse(SemanticModel model, ForStatementSyntax loop)
        {
            if (!(loop.Condition is BinaryExpressionSyntax condition)) return false;
            ISymbol counter = null;
            ExpressionSyntax initial = null;
            if (loop.Declaration != null && loop.Declaration.Variables.Count == 1 && loop.Declaration.Variables[0].Initializer != null)
            {
                counter = model.GetDeclaredSymbol(loop.Declaration.Variables[0]);
                initial = loop.Declaration.Variables[0].Initializer.Value;
            }
            else if (loop.Initializers.Count == 1 && loop.Initializers[0] is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                counter = model.GetSymbolInfo(assignment.Left).Symbol;
                initial = assignment.Right;
            }
            if (counter == null || !TryNumber(model, initial, out double start)) return false;

            bool counterOnLeft = SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(condition.Left).Symbol, counter);
            bool counterOnRight = SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(condition.Right).Symbol, counter);
            if (counterOnLeft == counterOnRight || !TryNumber(model, counterOnLeft ? condition.Right : condition.Left, out double limit)) return false;

            double left = counterOnLeft ? start : limit;
            double right = counterOnLeft ? limit : start;
            switch (condition.Kind())
            {
                case SyntaxKind.LessThanExpression: return !(left < right);
                case SyntaxKind.LessThanOrEqualExpression: return !(left <= right);
                case SyntaxKind.GreaterThanExpression: return !(left > right);
                case SyntaxKind.GreaterThanOrEqualExpression: return !(left >= right);
                default: return false;
            }
        }

        private static bool TryNumber(SemanticModel model, ExpressionSyntax expression, out double number)
        {
            number = 0;
            Optional<object> value = model.GetConstantValue(expression);
            if (!value.HasValue || value.Value == null || value.Value is bool || value.Value is string) return false;
            number = Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
    }
}