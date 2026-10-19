using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1018: a ?: inside another ?:, anywhere in its condition or branches.
    //
    // "hp > 50 ? Green : hp > 20 ? Yellow : Red" takes a moment to parse every time; an if chain or a
    // switch expression says the same thing plainly.
    //
    // Every nested ?: is reported, including ones inside method arguments, indexers, array
    // initializers and string interpolations. Lambdas, anonymous methods and local functions start
    // fresh.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NestedTernaryAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1018",
            "Nested ternary operator",
            "Replace this nested ?: with an if chain or a switch expression",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                for (SyntaxNode ancestor = context.Node.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    if (ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax
                        || ancestor is StatementSyntax || ancestor is MemberDeclarationSyntax) return;
                    if (!(ancestor is ConditionalExpressionSyntax)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
                    return;
                }
            }), SyntaxKind.ConditionalExpression);
        }
    }
}