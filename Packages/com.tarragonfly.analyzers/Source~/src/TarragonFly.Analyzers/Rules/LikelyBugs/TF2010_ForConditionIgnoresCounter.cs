using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2010: a for loop whose update step changes nothing its condition tests, as in "for (int i = 0;
    // i < count; j++)".
    //
    // Usually a copy-paste slip in nested loops; the loop then never ends or ends for the wrong reason.
    //
    // The update writes a variable when it assigns, increments or decrements it, or passes it as a ref
    // or out argument. Loops without an update are skipped. Reported on the condition, or on the for
    // keyword when there is none or it is a constant.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ForConditionIgnoresCounterAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2010",
            "For loop updates the wrong variable",
            "This loop's condition doesn't test anything its update step changes",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ForStatementSyntax loop = (ForStatementSyntax)context.Node;
                if (loop.Incrementors.Count == 0) return;

                HashSet<ISymbol> updated = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
                foreach (ExpressionSyntax incrementor in loop.Incrementors) AddWrittenSymbols(context.SemanticModel, incrementor, updated);

                if (loop.Condition != null)
                {
                    foreach (IdentifierNameSyntax name in loop.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                    {
                        ISymbol symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
                        if (symbol != null && updated.Contains(symbol)) return;
                    }
                }

                bool constant = loop.Condition != null && context.SemanticModel.GetConstantValue(loop.Condition, context.CancellationToken).HasValue;
                Location location = loop.Condition == null || constant ? loop.ForKeyword.GetLocation() : loop.Condition.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(Rule, location));
            }), SyntaxKind.ForStatement);
        }

        private static void AddWrittenSymbols(SemanticModel model, ExpressionSyntax incrementor, HashSet<ISymbol> written)
        {
            foreach (SyntaxNode node in incrementor.DescendantNodesAndSelf())
            {
                ExpressionSyntax target;
                switch (node)
                {
                    case AssignmentExpressionSyntax assignment: target = assignment.Left; break;
                    case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression): target = prefix.Operand; break;
                    case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression): target = postfix.Operand; break;
                    case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None): target = argument.Expression; break;
                    default: continue;
                }
                ISymbol symbol = model.GetSymbolInfo(target).Symbol;
                if (symbol != null) written.Add(symbol);
            }
        }
    }
}