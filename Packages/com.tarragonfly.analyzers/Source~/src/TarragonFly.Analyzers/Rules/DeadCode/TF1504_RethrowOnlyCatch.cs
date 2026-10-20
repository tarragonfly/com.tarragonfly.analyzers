using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1504: a catch clause that only does "throw;", which changes nothing.
    //
    // It's dead weight that suggests the exception gets handled when it doesn't.
    //
    // Kept when it matters: a rethrow-only catch is fine when a later catch of the same try would
    // otherwise handle that exception, i.e. a later catch of the same type, a base type, or catch-all,
    // that does more than rethrow. Catches with a "when" filter aren't checked. Comments don't count as
    // handling. Reported on the whole catch clause. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RethrowOnlyCatchAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1504",
            "Catch that only rethrows",
            "This catch only rethrows; remove it",
            "DeadCode",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TryStatementSyntax tryStatement = (TryStatementSyntax)context.Node;
                SyntaxList<CatchClauseSyntax> catches = tryStatement.Catches;
                for (int i = 0; i < catches.Count; i++)
                {
                    if (!OnlyRethrows(catches[i]) || ShieldsLaterCatch(context.SemanticModel, catches, i)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, catches[i].GetLocation()));
                }
            }), SyntaxKind.TryStatement);
        }

        private static bool OnlyRethrows(CatchClauseSyntax catchClause)
        {
            return catchClause.Filter == null
                && catchClause.Block.Statements.Count == 1
                && catchClause.Block.Statements[0] is ThrowStatementSyntax throwStatement
                && throwStatement.Expression == null;
        }

        // A later catch that does real work and would otherwise receive this catch's exceptions.
        private static bool ShieldsLaterCatch(SemanticModel model, SyntaxList<CatchClauseSyntax> catches, int index)
        {
            ITypeSymbol caught = CaughtType(model, catches[index]);
            for (int later = index + 1; later < catches.Count; later++)
            {
                if (OnlyRethrows(catches[later])) continue;
                ITypeSymbol laterType = CaughtType(model, catches[later]);
                if (laterType == null || caught == null || DerivesFrom(caught, laterType)) return true;
            }
            return false;
        }

        // Null for catch-all.
        private static ITypeSymbol CaughtType(SemanticModel model, CatchClauseSyntax catchClause)
        {
            return catchClause.Declaration == null ? null : model.GetTypeInfo(catchClause.Declaration.Type).Type;
        }

        private static bool DerivesFrom(ITypeSymbol type, ITypeSymbol baseType)
        {
            for (ITypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
            }
            return false;
        }
    }
}