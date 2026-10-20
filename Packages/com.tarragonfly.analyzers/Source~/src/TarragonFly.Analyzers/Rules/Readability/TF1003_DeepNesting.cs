using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1003: if, switch, for, foreach, while, do and try statements nested deeper than max_nesting
    // (default 3).
    //
    // Deep nesting hides which condition a line runs under. Early returns, guard clauses and small
    // helper methods flatten it.
    //
    // An else if continues its chain and adds no level. lock, using, fixed and plain blocks don't
    // count. Lambdas and local functions keep counting from where they're declared. Only the first
    // statement past the limit is reported, not everything inside it.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DeepNestingAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1003",
            "Control flow nested too deeply",
            "This statement is nested more than {0} levels deep; flatten it with early returns or a helper method",
            "Readability",
            CodeKind.All,
            description: "Counts if, switch, for, foreach, while, do and try statements. The limit is max_nesting in the settings file.");

        private static readonly SyntaxKind[] NestingKinds =
        {
            SyntaxKind.IfStatement, SyntaxKind.SwitchStatement, SyntaxKind.ForStatement, SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement, SyntaxKind.DoStatement, SyntaxKind.TryStatement
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxNesting;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                if (!AddsLevel(context.Node)) return;
                int depth = 1;
                for (SyntaxNode ancestor = context.Node.Parent; ancestor != null && !(ancestor is MemberDeclarationSyntax); ancestor = ancestor.Parent)
                {
                    if (AddsLevel(ancestor)) depth++;
                }
                if (depth == limit + 1) context.ReportDiagnostic(Diagnostic.Create(Rule, Keyword(context.Node).GetLocation(), limit));
            }), NestingKinds);
        }

        private static bool AddsLevel(SyntaxNode node)
        {
            if (node is IfStatementSyntax ifStatement) return Branches.IsChainStart(ifStatement);
            foreach (SyntaxKind kind in NestingKinds)
            {
                if (node.IsKind(kind)) return true;
            }
            return false;
        }

        private static SyntaxToken Keyword(SyntaxNode statement)
        {
            switch (statement)
            {
                case IfStatementSyntax s: return s.IfKeyword;
                case SwitchStatementSyntax s: return s.SwitchKeyword;
                case ForStatementSyntax s: return s.ForKeyword;
                case CommonForEachStatementSyntax s: return s.ForEachKeyword;
                case WhileStatementSyntax s: return s.WhileKeyword;
                case DoStatementSyntax s: return s.DoKeyword;
                default: return ((TryStatementSyntax)statement).TryKeyword;
            }
        }
    }
}