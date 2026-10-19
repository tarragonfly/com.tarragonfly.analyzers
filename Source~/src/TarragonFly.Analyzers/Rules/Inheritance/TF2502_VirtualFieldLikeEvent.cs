using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2502: a field-like event declared virtual, as in "public virtual event Action Died;".
    //
    // An override declares a second, separate event: handlers subscribed through the derived class
    // never hear the base class raise it, and the other way round.
    //
    // Abstract events and virtual events with explicit add and remove are fine. Reported on "virtual".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class VirtualFieldLikeEventAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2502",
            "Virtual field-like event",
            "Don't make this event virtual; an override creates a separate event and handlers miss raises from the other class",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                foreach (SyntaxToken modifier in ((EventFieldDeclarationSyntax)context.Node).Modifiers)
                {
                    if (modifier.IsKind(SyntaxKind.VirtualKeyword)) context.ReportDiagnostic(Diagnostic.Create(Rule, modifier.GetLocation()));
                }
            }), SyntaxKind.EventFieldDeclaration);
        }
    }
}