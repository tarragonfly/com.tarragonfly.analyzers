using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.OdinInspector
{
    // TF6001: an Odin attribute names a member of the wrong type, as in "[ShowIf("health")]" for a
    // float field, "[ValidateInput("CheckName")]" for a void method, or "[ValueDropdown("bossName")]"
    // for a string.
    //
    // Odin only finds out when it draws the Inspector, where it shows an error box instead of the
    // condition, the check or the dropdown, so the mistake hides until someone opens that object.
    //
    // Expected: ShowIf, HideIf, EnableIf and DisableIf with only a member name, InfoBox's VisibleIf
    // and ValidateInput need a bool field or property or a method returning bool; ShowIf("phase", 2)
    // and the like compare with a value, so any member works there. ValueDropdown needs a field,
    // property or method giving a collection (anything enumerable except a string). The member is
    // fine when any member with that name fits; TF6000 covers names that don't exist. Reported on the
    // string.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WrongOdinMemberTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6001",
            "Odin attribute names a member of the wrong type",
            "{0} needs {1}, but '{2}' {3}",
            "Odin",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                foreach (OdinMemberNames.NameInAttribute name in OdinMemberNames.On(context.Symbol, context.CancellationToken))
                {
                    if (!name.Location.IsInSource || !scope.IsAnalyzed(name.Location.SourceTree)) continue;
                    Frameworks.Odin.MemberValue needed = Frameworks.Odin.RequiredMemberValue(name.Attribute, name.Reference);
                    if (needed == Frameworks.Odin.MemberValue.Any) continue;
                    List<ISymbol> members = OdinMemberNames.Find(name.LookedUpOn, name.Reference.Member);
                    if (members.Count == 0 || members.Any(m => Fits(m, needed))) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, name.Location, name.AttributeName, Describe(needed), name.Reference.Member, Describe(members[0])));
                }
            }, SymbolKind.Field, SymbolKind.Property, SymbolKind.Method, SymbolKind.NamedType);
        }

        private static bool Fits(ISymbol member, Frameworks.Odin.MemberValue needed)
        {
            ITypeSymbol type = ValueType(member);
            if (type == null) return false;
            if (needed == Frameworks.Odin.MemberValue.Bool) return type.SpecialType == SpecialType.System_Boolean;
            return type.SpecialType != SpecialType.System_String
                && (type is IArrayTypeSymbol || type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable) || type.SpecialType == SpecialType.System_Collections_IEnumerable);
        }

        private static ITypeSymbol ValueType(ISymbol member)
        {
            switch (member)
            {
                case IFieldSymbol field: return field.Type;
                case IPropertySymbol property: return property.Type;
                case IMethodSymbol method: return method.ReturnType;
                default: return null;
            }
        }

        private static string Describe(Frameworks.Odin.MemberValue needed)
        {
            return needed == Frameworks.Odin.MemberValue.Bool
                ? "a bool field or property, or a method returning bool"
                : "a field, property or method that gives a collection";
        }

        private static string Describe(ISymbol member)
        {
            string type = ValueType(member)?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? "unknown";
            return member is IMethodSymbol ? $"returns {type}" : $"is {type}";
        }
    }
}