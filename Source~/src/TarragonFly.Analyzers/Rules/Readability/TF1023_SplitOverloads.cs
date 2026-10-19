using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1023: overloads of one method, or a type's constructors, that aren't next to each other in the
    // type.
    //
    // Readers look for the other versions of PlayClip right below the first one; spread out, one
    // gets missed or changed alone.
    //
    // Overloads are grouped by name, static or not, and accessibility, so a public and a private
    // overload may sit apart. Any member in between splits a group, fields and nested types included.
    // Operators and explicit interface implementations aren't checked. Reported once per group, on the
    // first overload.
    //
    // A partial type is checked across all its parts, as if they were one file, so overloads split
    // between two partial files are reported.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SplitOverloadsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1023",
            "Overloads are not together",
            "Keep the overloads of '{0}' next to each other",
            "Readability",
            CodeKind.All);

        private static readonly string[] AccessibilityModifiers = { "public", "protected", "internal", "private" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context => Analyze(context, scope), SymbolKind.NamedType);
        }

        private static void Analyze(SymbolAnalysisContext context, CompilationScope scope)
        {
            INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;

            // Members of every part in order; a part boundary counts as a member in between.
            List<MemberDeclarationSyntax> members = new List<MemberDeclarationSyntax>();
            foreach (SyntaxReference reference in type.DeclaringSyntaxReferences)
            {
                if (!(reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax part)) continue;
                if (members.Count > 0) members.Add(null);
                members.AddRange(part.Members);
            }

            Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>();
            List<string> order = new List<string>();
            for (int i = 0; i < members.Count; i++)
            {
                string key = GroupKey(members[i]);
                if (key == null) continue;
                if (!groups.TryGetValue(key, out List<int> positions))
                {
                    groups[key] = positions = new List<int>();
                    order.Add(key);
                }
                positions.Add(i);
            }

            foreach (string key in order)
            {
                List<int> positions = groups[key];
                if (positions[positions.Count - 1] - positions[0] == positions.Count - 1) continue;

                MemberDeclarationSyntax first = members[positions[0]];
                if (!scope.IsAnalyzed(first.SyntaxTree)) continue;
                SyntaxToken name = Name(first);
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), name.ValueText));
            }
        }

        // Null for members that are not grouped.
        private static string GroupKey(MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method when method.ExplicitInterfaceSpecifier == null && !IsPartialImplementation(method):
                    return "method " + method.Identifier.ValueText + Modifiers(method);
                case ConstructorDeclarationSyntax constructor:
                    return "constructor" + Modifiers(constructor);
                default:
                    return null;
            }
        }

        // The body half of a partial method is the same method as its declaration, not an overload.
        private static bool IsPartialImplementation(MethodDeclarationSyntax method)
        {
            return method.Modifiers.Any(SyntaxKind.PartialKeyword) && (method.Body != null || method.ExpressionBody != null);
        }

        // Accessibility and static, in a fixed order so "static public" matches "public static". No
        // accessibility modifier means the default: public in an interface, private elsewhere.
        private static string Modifiers(MemberDeclarationSyntax member)
        {
            string key = "";
            foreach (string accessibility in AccessibilityModifiers)
            {
                if (HasModifier(member, accessibility)) key += " " + accessibility;
            }
            if (key.Length == 0) key = member.Parent is InterfaceDeclarationSyntax ? " public" : " private";
            return HasModifier(member, "static") ? key + " static" : key;
        }

        private static bool HasModifier(MemberDeclarationSyntax member, string modifier)
        {
            foreach (SyntaxToken token in member.Modifiers)
            {
                if (token.ValueText == modifier) return true;
            }
            return false;
        }

        private static SyntaxToken Name(MemberDeclarationSyntax member)
        {
            return member is MethodDeclarationSyntax method ? method.Identifier : ((ConstructorDeclarationSyntax)member).Identifier;
        }
    }
}