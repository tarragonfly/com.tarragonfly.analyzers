using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1010: a switch section with more statements than max_switch_section_statements (default 8).
    //
    // A long case hides the shape of the switch, such as a state machine's states. Move the work into
    // a method named after the case.
    //
    // Counts every statement in the section, nested ones and those in lambdas included, but not the
    // braces of blocks; break and return count. Statements are counted, not lines. Reported on the
    // section's first label. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LongSwitchSectionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1010",
            "Switch section is too long",
            "This switch section has {0} statements, more than the limit of {1}; move the work into a method",
            "Readability",
            CodeKind.Game,
            description: "The limit is max_switch_section_statements in the settings file.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxSwitchSectionStatements;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SwitchSectionSyntax section = (SwitchSectionSyntax)context.Node;
                int statements = 0;
                foreach (SyntaxNode node in section.DescendantNodes())
                {
                    if (node is StatementSyntax && !(node is BlockSyntax)) statements++;
                }
                if (statements > limit) context.ReportDiagnostic(Diagnostic.Create(Rule, section.Labels[0].GetLocation(), statements, limit));
            }), SyntaxKind.SwitchSection);
        }
    }
}