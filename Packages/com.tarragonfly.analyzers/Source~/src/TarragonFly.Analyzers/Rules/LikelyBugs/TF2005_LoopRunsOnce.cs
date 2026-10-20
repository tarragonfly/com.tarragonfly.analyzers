using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2005: a loop whose body always ends with break, return or throw, as in "foreach (Enemy enemy in
    // enemies) { enemy.Alert(); break; }", or with a continue.
    //
    // A loop ending in break, return or throw runs at most once, so either the jump was meant to sit
    // inside an if, or the loop should be an if over the first item. A continue at the very end does
    // nothing at all.
    //
    // Only a jump that is the last statement of the body itself counts; jumps inside an if or else are
    // fine, and so are a break, return or throw when the body has a continue for this loop. for,
    // foreach, while and do loops count. Reported on the jump.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LoopRunsOnceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2005",
            "Loop that runs at most once",
            "{0}",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                StatementSyntax body = context.Node is ForStatementSyntax forLoop ? forLoop.Statement
                    : context.Node is CommonForEachStatementSyntax forEach ? forEach.Statement
                    : context.Node is WhileStatementSyntax whileLoop ? whileLoop.Statement
                    : ((DoStatementSyntax)context.Node).Statement;
                StatementSyntax last = body is BlockSyntax block ? (block.Statements.Count > 0 ? block.Statements[block.Statements.Count - 1] : null) : body;
                if (last is ContinueStatementSyntax)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, last.GetLocation(), "This continue is the last statement of the loop, so it does nothing; remove it"));
                    return;
                }
                if (!(last is BreakStatementSyntax || last is ReturnStatementSyntax || last is ThrowStatementSyntax)) return;
                if (ContinuesThisLoop(body)) return;
                string jump = last is BreakStatementSyntax ? "break" : last is ReturnStatementSyntax ? "return" : "throw";
                context.ReportDiagnostic(Diagnostic.Create(Rule, last.GetLocation(), "This " + jump + " ends the first pass of the loop every time, so the loop never repeats"));
            }), SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement, SyntaxKind.WhileStatement, SyntaxKind.DoStatement);
        }

        // A continue in the body that belongs to this loop, not to a loop nested inside it.
        private static bool ContinuesThisLoop(SyntaxNode node)
        {
            foreach (SyntaxNode child in node.ChildNodes())
            {
                if (child is ContinueStatementSyntax) return true;
                bool nestedLoop = child is ForStatementSyntax || child is CommonForEachStatementSyntax || child is WhileStatementSyntax || child is DoStatementSyntax;
                if (nestedLoop || child is AnonymousFunctionExpressionSyntax || child is LocalFunctionStatementSyntax) continue;
                if (ContinuesThisLoop(child)) return true;
            }
            return false;
        }
    }
}