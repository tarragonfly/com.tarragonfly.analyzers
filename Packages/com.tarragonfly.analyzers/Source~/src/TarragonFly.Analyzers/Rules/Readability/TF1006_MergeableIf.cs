using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1006: an if whose only content is another if, with no else on either, so the two conditions can
    // be joined with &&.
    //
    // One condition reads faster than two nested ones and saves a level of nesting.
    //
    // The outer if may be an else if. Comments around the inner if don't stop the merge. A pair whose
    // joined condition would have more &&, || and ?: operators than max_condition_operators isn't
    // reported, since TF1007 would then report the merged condition. Reported on the inner if keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MergeableIfAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1006",
            "If statements can be merged",
            "Merge this if into the one around it with &&",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxConditionOperators;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                IfStatementSyntax inner = (IfStatementSyntax)context.Node;
                if (inner.Else != null) return;

                // The outer if's body: the inner if itself, or a block holding nothing else.
                SyntaxNode body = inner.Parent is BlockSyntax block && block.Statements.Count == 1 ? block : (SyntaxNode)inner;
                if (!(body.Parent is IfStatementSyntax outer) || outer.Statement != body || outer.Else != null) return;

                int merged = ComplexConditionAnalyzer.CountOperators(outer.Condition) + ComplexConditionAnalyzer.CountOperators(inner.Condition) + 1;
                if (merged > limit) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, inner.IfKeyword.GetLocation()));
            }), SyntaxKind.IfStatement);
        }
    }
}