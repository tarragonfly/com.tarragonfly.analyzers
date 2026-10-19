using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.SilentFailures
{
    // TF3003: catching NullReferenceException.
    //
    // It hides the actual bug, a missing reference (often an unassigned Inspector field), instead of
    // checking for null where it can happen. Unity objects make this worse: a destroyed object can
    // throw MissingReferenceException in one place and NullReferenceException in another.
    //
    // Filters and rethrows don't change anything. Reported on the caught type. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CatchNullReferenceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3003",
            "Catching NullReferenceException",
            "Check for null where it can happen instead of catching NullReferenceException",
            "SilentFailures",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeSyntax type = ((CatchDeclarationSyntax)context.Node).Type;
                if (SymbolNames.IsNamed(context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type, "System.NullReferenceException"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, type.GetLocation()));
                }
            }), SyntaxKind.CatchDeclaration);
        }
    }
}