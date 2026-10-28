using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2063: [Flags] on an enum with a value that is neither a single bit nor a combination of the
    // enum's single-bit members, such as 5 when only 1 and 2 are defined.
    //
    // Either the enum isn't really a set of flags and [Flags] makes ToString() and HasFlag lie, or a
    // flag for the missing bit was forgotten.
    //
    // 0 and -1 (all bits) are fine. Reported on the enum name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FlagsWithNonFlagValuesAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2063",
            "[Flags] on an enum that isn't flags",
            "'{0}' has values that aren't single bits or combinations of its flags; remove [Flags] or add the missing flags",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                EnumDeclarationSyntax declaration = (EnumDeclarationSyntax)context.Node;
                INamedTypeSymbol type = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
                if (!FlagsEnums.IsFlags(type)) return;

                List<ulong> values = new List<ulong>();
                ulong flags = 0;
                foreach (EnumMemberDeclarationSyntax member in declaration.Members)
                {
                    ulong? value = FlagsEnums.Value(context.SemanticModel, member);
                    if (value == null) return;
                    values.Add(value.Value);
                    if (FlagsEnums.IsSingleBit(value.Value)) flags |= value.Value;
                }

                foreach (ulong value in values)
                {
                    if (value == 0 || value == FlagsEnums.AllBits(type) || (value & ~flags) == 0) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), declaration.Identifier.ValueText));
                    return;
                }
            }), SyntaxKind.EnumDeclaration);
        }
    }
}
