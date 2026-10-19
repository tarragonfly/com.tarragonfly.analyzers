using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2028: a [Flags] enum whose 0 value isn't named None.
    //
    // 0 means "no flags set", and "HasFlag(Nothing)" is true for every value, so a name other than
    // None invites code that tests for it like a real flag.
    //
    // Fine when any 0 member is called None, even if another 0 member has a different name. 0 can come
    // from automatic numbering or an expression such as "1 - 1". Reported on each 0 member.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FlagsZeroNotNoneAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2028",
            "[Flags] enum zero value not named None",
            "Name the 0 value of this [Flags] enum None",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                EnumDeclarationSyntax declaration = (EnumDeclarationSyntax)context.Node;
                if (!FlagsEnums.IsFlags(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken))) return;

                List<EnumMemberDeclarationSyntax> zeros = new List<EnumMemberDeclarationSyntax>();
                foreach (EnumMemberDeclarationSyntax member in declaration.Members)
                {
                    if (FlagsEnums.Value(context.SemanticModel, member) != 0) continue;
                    if (member.Identifier.ValueText == "None") return;
                    zeros.Add(member);
                }
                foreach (EnumMemberDeclarationSyntax zero in zeros) context.ReportDiagnostic(Diagnostic.Create(Rule, zero.GetLocation()));
            }), SyntaxKind.EnumDeclaration);
        }
    }
}