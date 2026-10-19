using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    // TF3050: a throw statement inside a finally block.
    //
    // If the try already failed, the new exception replaces the original one, and the error you see
    // in the Unity Console isn't the one that went wrong.
    //
    // Throw expressions (x ?? throw ...) aren't checked. Reported on the throw statement. Game code
    // only.
    //
    // Throws that can't escape the finally are skipped:
    //   - throws inside a lambda, anonymous method or local function declared in the finally run when
    //     it's called, not as part of the finally;
    //   - throws inside a try within the finally whose catch (without a filter) takes that exception
    //     type, or is catch-all, are handled there.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThrowInFinallyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3050",
            "Throw inside finally",
            "An exception thrown from finally replaces any exception already on its way out",
            "Exceptions",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ThrowStatementSyntax throwStatement = (ThrowStatementSyntax)context.Node;
                ITypeSymbol thrown = throwStatement.Expression == null ? null : context.SemanticModel.GetTypeInfo(throwStatement.Expression, context.CancellationToken).Type;

                SyntaxNode child = throwStatement;
                for (SyntaxNode ancestor = throwStatement.Parent; ancestor != null; child = ancestor, ancestor = ancestor.Parent)
                {
                    if (ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax || ancestor is MemberDeclarationSyntax) return;
                    if (ancestor is TryStatementSyntax tryStatement && tryStatement.Block == child && CatchClauses.Catch(context.SemanticModel, tryStatement, thrown)) return;
                    if (!(ancestor is FinallyClauseSyntax)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, throwStatement.GetLocation()));
                    return;
                }
            }), SyntaxKind.ThrowStatement);
        }
    }
}