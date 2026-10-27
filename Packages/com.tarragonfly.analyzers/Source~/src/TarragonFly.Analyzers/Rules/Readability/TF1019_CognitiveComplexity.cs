using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1019: a method that is hard to follow, scored by cognitive complexity, above 15 by default
    // (max_cognitive_complexity in the settings file).
    //
    // The score counts the places where a reader has to stop and think, and charges more for each
    // level of nesting, so deeply nested decision code scores high even when it is short. Split the
    // method, use early returns, or move branches into well-named helpers.
    //
    // Scoring:
    //   - +1, plus 1 per enclosing nesting level: if, ?:, switch statements and expressions, for,
    //     foreach, while, do and catch.
    //   - +1 flat: else if, else, goto, and each sequence of the same logical operator, read from
    //     left to right through parentheses. This includes &&, || and "and" or "or" patterns:
    //     "a && b && c" is +1, "a && b || c" is +2 and "a || b && c || d" is +3. An operator under
    //     a "!" or "not" starts a separate sequence.
    //   - +1 once per method or local function that calls itself directly.
    //   - One nesting level is added inside the bodies of if, else, switch sections, loops, catch
    //     blocks, lambdas and local functions, and throughout a ?: expression. Statement conditions
    //     sit at the level of their statement, and an else if body at the level of the if body.
    //   - Nothing for try, finally, break, continue, ??, ?. or "is not".
    // Methods, constructors, destructors, operators and property, indexer and event accessors are
    // scored separately, and so is a field declaration with initializers, such as a lambda kept in a
    // field; local functions and lambdas count toward the member around them. Reported on the member
    // name, the accessor keyword, the operator token or the field's first variable.
    // Indirect recursion and preprocessor conditionals are not counted. Revisit if either causes
    // complex methods to pass project checks.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CognitiveComplexityAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1019",
            "Method too complex to follow",
            "'{0}' has a cognitive complexity of {1}, above the {2} allowed; split it or flatten its nesting",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxCognitiveComplexity;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxNode body = Body(context.Node, out SyntaxToken location, out string name);
                if (body == null) return;
                IMethodSymbol self = context.Node is BaseMethodDeclarationSyntax
                    ? context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) as IMethodSymbol : null;
                ComplexityWalker walker = new ComplexityWalker(context.SemanticModel, self, context.CancellationToken);
                walker.Visit(body);
                if (walker.Score > limit) context.ReportDiagnostic(Diagnostic.Create(Rule, location.GetLocation(), name, walker.Score, limit));
            }), SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration, SyntaxKind.DestructorDeclaration, SyntaxKind.OperatorDeclaration,
                SyntaxKind.ConversionOperatorDeclaration, SyntaxKind.GetAccessorDeclaration, SyntaxKind.SetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration,
                SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration,
                SyntaxKind.FieldDeclaration);
        }

        // The code to score and where to report it; null for members without a body.
        private static SyntaxNode Body(SyntaxNode member, out SyntaxToken location, out string name)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method:
                    location = method.Identifier;
                    name = method.Identifier.ValueText;
                    return (SyntaxNode)method.Body ?? method.ExpressionBody;
                case ConstructorDeclarationSyntax constructor:
                    location = constructor.Identifier;
                    name = constructor.Identifier.ValueText;
                    return (SyntaxNode)constructor.Body ?? constructor.ExpressionBody;
                case DestructorDeclarationSyntax destructor:
                    location = destructor.Identifier;
                    name = "~" + destructor.Identifier.ValueText;
                    return (SyntaxNode)destructor.Body ?? destructor.ExpressionBody;
                case OperatorDeclarationSyntax op:
                    location = op.OperatorToken;
                    name = "operator " + op.OperatorToken.Text;
                    return (SyntaxNode)op.Body ?? op.ExpressionBody;
                case ConversionOperatorDeclarationSyntax conversion:
                    location = conversion.OperatorKeyword;
                    name = "operator " + conversion.Type;
                    return (SyntaxNode)conversion.Body ?? conversion.ExpressionBody;
                case AccessorDeclarationSyntax accessor:
                    location = accessor.Keyword;
                    name = accessor.Keyword.Text;
                    return (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
                case PropertyDeclarationSyntax property:
                    location = property.Identifier;
                    name = property.Identifier.ValueText;
                    return property.ExpressionBody;
                case IndexerDeclarationSyntax indexer:
                    location = indexer.ThisKeyword;
                    name = "this[]";
                    return indexer.ExpressionBody;
                case FieldDeclarationSyntax field:
                    location = field.Declaration.Variables[0].Identifier;
                    name = location.ValueText;
                    return field.Declaration.Variables.Any(variable => variable.Initializer != null) ? field.Declaration : null;
                default:
                    location = default;
                    name = null;
                    return null;
            }
        }

        private sealed class ComplexityWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel model;
            private IMethodSymbol currentMethod;
            private bool hasRecursiveCall;
            private readonly CancellationToken ct;
            private int nesting;

            public ComplexityWalker(SemanticModel model, IMethodSymbol self, CancellationToken ct)
            {
                this.model = model;
                currentMethod = self;
                this.ct = ct;
            }

            public int Score { get; private set; }

            public override void VisitIfStatement(IfStatementSyntax node)
            {
                Score += node.Parent is ElseClauseSyntax ? 1 : 1 + nesting;
                Visit(node.Condition);
                Nested(node.Statement);
                if (node.Else == null) return;
                if (node.Else.Statement is IfStatementSyntax elseIf)
                {
                    Visit(elseIf);
                    return;
                }
                Score += 1;
                Nested(node.Else.Statement);
            }

            public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
            {
                Score += 1 + nesting;
                Nested(node.Condition);
                Nested(node.WhenTrue);
                Nested(node.WhenFalse);
            }

            public override void VisitSwitchStatement(SwitchStatementSyntax node)
            {
                Score += 1 + nesting;
                Visit(node.Expression);
                foreach (SwitchSectionSyntax section in node.Sections) Nested(section);
            }

            public override void VisitSwitchExpression(SwitchExpressionSyntax node)
            {
                Score += 1 + nesting;
                Visit(node.GoverningExpression);
                foreach (SwitchExpressionArmSyntax arm in node.Arms) Nested(arm);
            }

            public override void VisitForStatement(ForStatementSyntax node)
            {
                Score += 1 + nesting;
                if (node.Declaration != null) Visit(node.Declaration);
                foreach (ExpressionSyntax initializer in node.Initializers) Visit(initializer);
                if (node.Condition != null) Visit(node.Condition);
                foreach (ExpressionSyntax incrementor in node.Incrementors) Visit(incrementor);
                Nested(node.Statement);
            }

            public override void VisitForEachStatement(ForEachStatementSyntax node)
            {
                Score += 1 + nesting;
                Visit(node.Expression);
                Nested(node.Statement);
            }

            public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
            {
                Score += 1 + nesting;
                Visit(node.Expression);
                Nested(node.Statement);
            }

            public override void VisitWhileStatement(WhileStatementSyntax node)
            {
                Score += 1 + nesting;
                Visit(node.Condition);
                Nested(node.Statement);
            }

            public override void VisitDoStatement(DoStatementSyntax node)
            {
                Score += 1 + nesting;
                Nested(node.Statement);
                Visit(node.Condition);
            }

            public override void VisitCatchClause(CatchClauseSyntax node)
            {
                Score += 1 + nesting;
                if (node.Filter != null) Visit(node.Filter);
                Nested(node.Block);
            }

            public override void VisitGotoStatement(GotoStatementSyntax node)
            {
                Score += 1;
                base.VisitGotoStatement(node);
            }

            public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => Nested(node.Body);

            public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => Nested(node.Body);

            public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => Nested(node.Body);

            public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
            {
                IMethodSymbol enclosingMethod = currentMethod;
                bool enclosingMethodHasRecursiveCall = hasRecursiveCall;
                currentMethod = model.GetDeclaredSymbol(node, ct) as IMethodSymbol;
                hasRecursiveCall = false;
                if (node.Body != null) Nested(node.Body);
                if (node.ExpressionBody != null) Nested(node.ExpressionBody);
                currentMethod = enclosingMethod;
                hasRecursiveCall = enclosingMethodHasRecursiveCall;
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (IsLogical(node))
                {
                    SyntaxKind previousOperator = SyntaxKind.None;
                    VisitLogicalSequence(node, ref previousOperator);
                }
                else
                {
                    base.VisitBinaryExpression(node);
                }
            }

            public override void VisitBinaryPattern(BinaryPatternSyntax node)
            {
                SyntaxKind previousOperator = SyntaxKind.None;
                VisitLogicalSequence(node, ref previousOperator);
            }

            private void VisitLogicalSequence(SyntaxNode node, ref SyntaxKind previousOperator)
            {
                SyntaxNode left;
                SyntaxNode right;
                switch (node)
                {
                    case ParenthesizedExpressionSyntax expression:
                        VisitLogicalSequence(expression.Expression, ref previousOperator);
                        return;
                    case ParenthesizedPatternSyntax pattern:
                        VisitLogicalSequence(pattern.Pattern, ref previousOperator);
                        return;
                    case BinaryExpressionSyntax binary when IsLogical(binary):
                        left = binary.Left;
                        right = binary.Right;
                        break;
                    case BinaryPatternSyntax binaryPattern:
                        left = binaryPattern.Left;
                        right = binaryPattern.Right;
                        break;
                    default:
                        Visit(node);
                        return;
                }

                VisitLogicalSequence(left, ref previousOperator);
                if (!node.IsKind(previousOperator)) Score += 1;
                previousOperator = node.Kind();
                VisitLogicalSequence(right, ref previousOperator);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (!hasRecursiveCall && currentMethod != null && model.GetSymbolInfo(node, ct).Symbol is IMethodSymbol called
                    && SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, currentMethod.OriginalDefinition))
                {
                    Score += 1;
                    hasRecursiveCall = true;
                }
                base.VisitInvocationExpression(node);
            }

            private void Nested(SyntaxNode node)
            {
                nesting++;
                Visit(node);
                nesting--;
            }

            private static bool IsLogical(SyntaxNode node)
            {
                return node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression);
            }
        }
    }
}
