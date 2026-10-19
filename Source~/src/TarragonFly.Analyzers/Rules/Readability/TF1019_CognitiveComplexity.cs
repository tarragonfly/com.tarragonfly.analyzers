using System.Collections.Generic;
using System.Collections.Immutable;
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
    //   - +1 flat: else if, else, goto, a call back into the method itself, and each run of && or ||
    //     (and of the "and" or "or" pattern combinators): "a && b && c" is +1, "a && b || c" is +2.
    //     A "!" or "not" starts a new run.
    //   - One nesting level is added inside the bodies of if, else, ?:, switch sections, loops, catch
    //     blocks, lambdas and local functions. Conditions sit at the level of their statement, and an
    //     else if body at the level of the if body.
    //   - Nothing for try, finally, break, continue, ??, ?. or "is not".
    // Methods, constructors, destructors, operators and property, indexer and event accessors are
    // scored separately; local functions and lambdas count toward the member around them. Reported
    // on the member name, the accessor keyword or the operator token.
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
                SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration, SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration);
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
                default:
                    location = default;
                    name = null;
                    return null;
            }
        }

        private sealed class ComplexityWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel model;
            private readonly IMethodSymbol self;
            private readonly CancellationToken ct;
            private int nesting;

            public ComplexityWalker(SemanticModel model, IMethodSymbol self, CancellationToken ct)
            {
                this.model = model;
                this.self = self;
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
                Visit(node.Condition);
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
                if (node.Body != null) Nested(node.Body);
                if (node.ExpressionBody != null) Nested(node.ExpressionBody);
            }

            public override void VisitBinaryExpression(BinaryExpressionSyntax node)
            {
                if (IsLogical(node) && !IsLogical(Unwrap(node.Parent)))
                {
                    List<SyntaxKind> operators = new List<SyntaxKind>();
                    CollectOperators(node, operators);
                    Score += Runs(operators);
                }
                base.VisitBinaryExpression(node);
            }

            public override void VisitBinaryPattern(BinaryPatternSyntax node)
            {
                SyntaxNode parent = node.Parent;
                while (parent is ParenthesizedPatternSyntax) parent = parent.Parent;
                if (!(parent is BinaryPatternSyntax))
                {
                    List<SyntaxKind> operators = new List<SyntaxKind>();
                    CollectPatternOperators(node, operators);
                    Score += Runs(operators);
                }
                base.VisitBinaryPattern(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                if (self != null && model.GetSymbolInfo(node, ct).Symbol is IMethodSymbol called
                    && SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, self.OriginalDefinition))
                {
                    Score += 1;
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

            private static SyntaxNode Unwrap(SyntaxNode node)
            {
                while (node is ParenthesizedExpressionSyntax) node = node.Parent;
                return node;
            }

            // The && and || operators of one expression in source order, through parentheses.
            private static void CollectOperators(ExpressionSyntax expression, List<SyntaxKind> operators)
            {
                while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
                if (!(expression is BinaryExpressionSyntax binary) || !IsLogical(binary)) return;
                CollectOperators(binary.Left, operators);
                operators.Add(binary.Kind());
                CollectOperators(binary.Right, operators);
            }

            private static void CollectPatternOperators(PatternSyntax pattern, List<SyntaxKind> operators)
            {
                while (pattern is ParenthesizedPatternSyntax parenthesized) pattern = parenthesized.Pattern;
                if (!(pattern is BinaryPatternSyntax binary)) return;
                CollectPatternOperators(binary.Left, operators);
                operators.Add(binary.Kind());
                CollectPatternOperators(binary.Right, operators);
            }

            // A run is a stretch of the same operator; "a && b || c && d" has three.
            private static int Runs(List<SyntaxKind> operators)
            {
                int runs = 0;
                for (int i = 0; i < operators.Count; i++)
                {
                    if (i == 0 || operators[i] != operators[i - 1]) runs++;
                }
                return runs;
            }
        }
    }
}