using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.OdinInspector
{
    // TF6000: an Odin attribute names a member that doesn't exist, as in "[ShowIf("isBos")]" when the
    // field is isBoss, or "[LabelText("$displayNmae")]".
    //
    // Odin finds the member by name when the Inspector draws, so a typo or a rename that missed the
    // string compiles fine. The Inspector then shows an error box, or always shows the field with no
    // error, and nobody notices until someone opens that object.
    //
    // Checked: the member-name argument of ShowIf, HideIf, EnableIf, DisableIf, ValueDropdown,
    // ValidateInput, OnValueChanged and InlineButton, InfoBox's visibleIfMemberName and VisibleIf,
    // ShowIf's Condition, and "$Name" at the start of any other Odin string argument, such as a label.
    // "@" expressions are C# snippets and are skipped, and so are "$property", "$value" and "$root",
    // which Odin provides itself. The name is looked up as a field, property or
    // method on the type that declares the attributed member, base types included, case-sensitively.
    // Reported on the string.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnknownOdinMemberAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6000",
            "Odin attribute names a member that doesn't exist",
            "{0} names '{1}', but '{2}' has no field, property or method with that name",
            "Odin",
            CodeKind.All);

        // "$property", "$value" and "$root" are values Odin provides itself, not members.
        private static readonly ImmutableHashSet<string> OdinNamedValues = ImmutableHashSet.Create("property", "value", "root");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                foreach (OdinMemberNames.NameInAttribute name in OdinMemberNames.On(context.Symbol, context.CancellationToken))
                {
                    if (!name.Location.IsInSource || !scope.IsAnalyzed(name.Location.SourceTree)) continue;
                    if (OdinNamedValues.Contains(name.Reference.Member) || OdinMemberNames.Find(name.LookedUpOn, name.Reference.Member).Count > 0) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, name.Location, name.AttributeName, name.Reference.Member, name.LookedUpOn.Name));
                }
            }, SymbolKind.Field, SymbolKind.Property, SymbolKind.Method, SymbolKind.NamedType);
        }
    }
}