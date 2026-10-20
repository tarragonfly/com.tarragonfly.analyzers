using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2040: a base class testing its own runtime type, as in "if (this is Boss)".
    //
    // The base class then needs updating for every new subclass; a virtual method the subclass
    // overrides keeps that knowledge where it belongs.
    //
    // Type tests and type patterns both count ("this is Boss boss"); "this as Boss" doesn't. Reported
    // on the whole test. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TypeTestOnThisAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2040",
            "Type test on this",
            "Don't test the type of 'this'; move the type-specific code into a virtual method the subclass overrides",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax tested = context.Node is BinaryExpressionSyntax binary ? binary.Left : ((IsPatternExpressionSyntax)context.Node).Expression;
                if (tested is ThisExpressionSyntax) context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation()));
            }), SyntaxKind.IsExpression, SyntaxKind.IsPatternExpression);
        }
    }
}