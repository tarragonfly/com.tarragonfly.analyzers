using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2027: a [Flags] enum with members that rely on automatic numbering past 1.
    //
    // Automatic values count 0, 1, 2, 3..., so the fourth member is 3, which is the first two
    // combined, and "abilities.HasFlag(Swim)" is true for anyone who can jump and run.
    //
    // Members left at 0 or 1 by numbering are fine, so "None, Jump" passes. Reported on the enum name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FlagsWithoutValuesAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2027",
            "[Flags] enum members without explicit values",
            "Give every member of '{0}' an explicit power-of-two value; automatic numbering makes some of them combinations",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                EnumDeclarationSyntax declaration = (EnumDeclarationSyntax)context.Node;
                if (!FlagsEnums.IsFlags(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken))) return;
                foreach (EnumMemberDeclarationSyntax member in declaration.Members)
                {
                    if (member.EqualsValue != null) continue;
                    ulong? value = FlagsEnums.Value(context.SemanticModel, member);
                    if (value == null || value <= 1) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), declaration.Identifier.ValueText));
                    return;
                }
            }), SyntaxKind.EnumDeclaration);
        }
    }
}