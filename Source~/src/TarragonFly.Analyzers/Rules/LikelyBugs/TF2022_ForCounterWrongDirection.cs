using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2022: a for loop whose counter moves away from the end its condition checks, as in "for (int i
    // = 0; i < count; i--)".
    //
    // The loop then runs zero times or until the counter wraps around, which freezes the game.
    //
    // The condition must compare the counter with <, <=, > or >=, on either side. Updates with a known
    // direction are ++, --, += and -= with a positive number literal, and "i = i + 1" / "i = i - 1"
    // style assignments. Anything else, such as "i += step" or "i -= -1", is left alone. Reported on
    // the wrong update.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ForCounterWrongDirectionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2022",
            "For loop counter moves the wrong way",
            "This update moves '{0}' away from the end the loop condition checks",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ForStatementSyntax loop = (ForStatementSyntax)context.Node;
                if (!(Unwrap(loop.Condition) is BinaryExpressionSyntax condition)) return;

                foreach (ExpressionSyntax incrementor in loop.Incrementors)
                {
                    int step = Step(incrementor, out ExpressionSyntax counter);
                    if (step == 0) continue;
                    int needed = NeededDirection(context.SemanticModel, condition, counter);
                    if (needed != 0 && needed != step)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, incrementor.GetLocation(), counter.ToString()));
                    }
                }
            }), SyntaxKind.ForStatement);
        }

        // +1 when the counter must grow to reach the end, -1 when it must shrink, 0 when unknown.
        private static int NeededDirection(SemanticModel model, BinaryExpressionSyntax condition, ExpressionSyntax counter)
        {
            int direction;
            switch (condition.Kind())
            {
                case SyntaxKind.LessThanExpression:
                case SyntaxKind.LessThanOrEqualExpression:
                    direction = 1;
                    break;
                case SyntaxKind.GreaterThanExpression:
                case SyntaxKind.GreaterThanOrEqualExpression:
                    direction = -1;
                    break;
                default:
                    return 0;
            }
            if (SameVariable(model, Unwrap(condition.Left), counter)) return direction;
            if (SameVariable(model, Unwrap(condition.Right), counter)) return -direction;
            return 0;
        }

        // +1 or -1 for an update with a known direction, 0 otherwise.
        private static int Step(ExpressionSyntax incrementor, out ExpressionSyntax counter)
        {
            counter = null;
            switch (incrementor)
            {
                case PostfixUnaryExpressionSyntax postfix:
                    counter = postfix.Operand;
                    return postfix.IsKind(SyntaxKind.PostIncrementExpression) ? 1 : postfix.IsKind(SyntaxKind.PostDecrementExpression) ? -1 : 0;
                case PrefixUnaryExpressionSyntax prefix:
                    counter = prefix.Operand;
                    return prefix.IsKind(SyntaxKind.PreIncrementExpression) ? 1 : prefix.IsKind(SyntaxKind.PreDecrementExpression) ? -1 : 0;
                case AssignmentExpressionSyntax assignment:
                    counter = assignment.Left;
                    return AssignmentStep(assignment);
                default:
                    return 0;
            }
        }

        private static int AssignmentStep(AssignmentExpressionSyntax assignment)
        {
            if (assignment.IsKind(SyntaxKind.AddAssignmentExpression)) return IsPositiveLiteral(assignment.Right) ? 1 : 0;
            if (assignment.IsKind(SyntaxKind.SubtractAssignmentExpression)) return IsPositiveLiteral(assignment.Right) ? -1 : 0;
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || !(Unwrap(assignment.Right) is BinaryExpressionSyntax value)) return 0;

            // i = i + 1, i = 1 + i, i = i - 1
            string counter = assignment.Left.ToString();
            if (value.IsKind(SyntaxKind.AddExpression))
            {
                bool counterPlusStep = value.Left.ToString() == counter && IsPositiveLiteral(value.Right);
                bool stepPlusCounter = value.Right.ToString() == counter && IsPositiveLiteral(value.Left);
                return counterPlusStep || stepPlusCounter ? 1 : 0;
            }
            if (value.IsKind(SyntaxKind.SubtractExpression)) return value.Left.ToString() == counter && IsPositiveLiteral(value.Right) ? -1 : 0;
            return 0;
        }

        private static bool IsPositiveLiteral(ExpressionSyntax expression)
        {
            return Unwrap(expression) is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.NumericLiteralExpression)
                && literal.Token.Value is object number
                && System.Convert.ToDouble(number, System.Globalization.CultureInfo.InvariantCulture) > 0;
        }

        private static bool SameVariable(SemanticModel model, ExpressionSyntax first, ExpressionSyntax second)
        {
            ISymbol firstSymbol = model.GetSymbolInfo(first).Symbol;
            return firstSymbol != null && SymbolEqualityComparer.Default.Equals(firstSymbol, model.GetSymbolInfo(second).Symbol);
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
            return expression;
        }
    }
}