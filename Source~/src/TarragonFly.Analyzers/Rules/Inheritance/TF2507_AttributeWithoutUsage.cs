using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2507: a custom attribute class with no [AttributeUsage], on itself or on a base attribute
    // class.
    //
    // Without it the attribute fits anywhere, so [ShowInStats] on a method instead of a field compiles
    // and silently does nothing.
    //
    // Abstract attribute classes aren't checked. Reported on the class name.
    //
    // [AttributeUsage] is inherited by derived attribute classes, so one on a base attribute class is
    // enough.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AttributeWithoutUsageAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2507",
            "Custom attribute without AttributeUsage",
            "Add [AttributeUsage] to '{0}' to say what it can be put on",
            "Inheritance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                if (type.TypeKind != TypeKind.Class || type.IsAbstract || !SymbolNames.IsOrDerivesFrom(type, "System.Attribute")) return;
                for (INamedTypeSymbol current = type; current != null && !SymbolNames.IsNamed(current, "System.Attribute"); current = current.BaseType)
                {
                    if (SymbolNames.HasAttribute(current, "System.AttributeUsageAttribute")) return;
                }

                Location location = type.Locations[0];
                if (location.IsInSource && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
            }, SymbolKind.NamedType);
        }
    }
}