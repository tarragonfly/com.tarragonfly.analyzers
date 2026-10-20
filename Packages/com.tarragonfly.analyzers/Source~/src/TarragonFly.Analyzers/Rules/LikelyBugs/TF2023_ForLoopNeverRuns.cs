using System;
using System.Collections.Immutable;
using System.Globalization;
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
    // A constant false condition, such as "!true" or "1 > 2", counts. Otherwise the condition must
    // compare (<, <=, >, >=, == or !=) a counter with a constant, named constants and enum members
    // included, or with itself, and the counter must start at a constant, from one of its declarators
    // or a simple assignment in the initializer. Whole numbers and chars are compared exactly.
    // Reported on the condition.
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
                if (StartsFalse(context.SemanticModel, loop))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, loop.Condition.GetLocation()));
                }
            }), SyntaxKind.ForStatement);
        }

        private static bool StartsFalse(SemanticModel model, ForStatementSyntax loop)
        {
            Optional<object> constant = model.GetConstantValue(loop.Condition);
            if (constant.HasValue) return constant.Value is bool value && !value;
            if (!(loop.Condition is BinaryExpressionSyntax condition)) return false;

            ISymbol leftCounter = model.GetSymbolInfo(condition.Left).Symbol;
            ISymbol rightCounter = model.GetSymbolInfo(condition.Right).Symbol;
            bool counterOnLeft = leftCounter != null && StartValue(model, loop, leftCounter) != null;
            bool counterOnRight = rightCounter != null && StartValue(model, loop, rightCounter) != null;
            if (!counterOnLeft && !counterOnRight) return false;
            if (counterOnLeft && counterOnRight && !SymbolEqualityComparer.Default.Equals(leftCounter, rightCounter)) return false;

            if (!TryNumber(model, counterOnLeft ? StartValue(model, loop, leftCounter) : condition.Left, out object left)) return false;
            if (!TryNumber(model, counterOnRight ? StartValue(model, loop, rightCounter) : condition.Right, out object right)) return false;
            return IsFalse(condition.Kind(), left, right);
        }

        // The constant-or-not expression the initializer gives the counter last, or null when the
        // initializer doesn't set it.
        private static ExpressionSyntax StartValue(SemanticModel model, ForStatementSyntax loop, ISymbol counter)
        {
            ExpressionSyntax start = null;
            if (loop.Declaration != null)
            {
                foreach (VariableDeclaratorSyntax variable in loop.Declaration.Variables)
                {
                    if (variable.Initializer != null && SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(variable), counter)) start = variable.Initializer.Value;
                }
            }
            foreach (ExpressionSyntax initializer in loop.Initializers)
            {
                if (initializer is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                    && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, counter))
                {
                    start = assignment.Right;
                }
            }
            return start;
        }

        private static bool TryNumber(SemanticModel model, ExpressionSyntax expression, out object number)
        {
            Optional<object> value = model.GetConstantValue(expression);
            number = value.HasValue ? value.Value : null;
            return number != null && !(number is bool) && !(number is string);
        }

        // Floating-point values compare as doubles, so NaN stays false against anything; whole numbers,
        // chars and decimals compare exactly, since a long near its limit doesn't fit a double.
        private static bool IsFalse(SyntaxKind comparison, object left, object right)
        {
            if (left is float || left is double || right is float || right is double)
            {
                double l = left is char leftCharacter ? leftCharacter : Convert.ToDouble(left, CultureInfo.InvariantCulture);
                double r = right is char rightCharacter ? rightCharacter : Convert.ToDouble(right, CultureInfo.InvariantCulture);
                switch (comparison)
                {
                    case SyntaxKind.LessThanExpression: return !(l < r);
                    case SyntaxKind.LessThanOrEqualExpression: return !(l <= r);
                    case SyntaxKind.GreaterThanExpression: return !(l > r);
                    case SyntaxKind.GreaterThanOrEqualExpression: return !(l >= r);
                    case SyntaxKind.EqualsExpression: return !(l == r);
                    case SyntaxKind.NotEqualsExpression: return !(l != r);
                    default: return false;
                }
            }
            int order = ToDecimal(left).CompareTo(ToDecimal(right));
            switch (comparison)
            {
                case SyntaxKind.LessThanExpression: return order >= 0;
                case SyntaxKind.LessThanOrEqualExpression: return order > 0;
                case SyntaxKind.GreaterThanExpression: return order <= 0;
                case SyntaxKind.GreaterThanOrEqualExpression: return order < 0;
                case SyntaxKind.EqualsExpression: return order != 0;
                case SyntaxKind.NotEqualsExpression: return order == 0;
                default: return false;
            }
        }

        // Convert.ToDecimal and Convert.ToDouble throw on a char, so chars go through their code.
        private static decimal ToDecimal(object value)
        {
            return value is char character ? character : Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
    }
}