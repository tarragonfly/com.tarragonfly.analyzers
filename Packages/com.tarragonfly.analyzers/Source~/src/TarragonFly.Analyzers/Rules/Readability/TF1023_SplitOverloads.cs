using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
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
    // Overloads are grouped by name, static or not, abstract or not, and accessibility, so a public
    // and a private overload, or an abstract and a concrete one, may sit apart. Any member in between
    // splits a group, fields and nested types included, except other overloads of the same name. An
    // explicit interface implementation isn't an overload, so "int IShop.Buy(string item)" between
    // two Buy overloads splits them too.
    // Classes often keep one section per interface they implement. So an overload may sit apart from
    // the one before it, whatever is in between, when it implements a member of an interface that one
    // doesn't and sits next to another member of that interface. Overloads that belong to the same
    // interface still have to be together. Operators and explicit interface implementations aren't
    // checked. Reported once per group, on the first overload.
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

            Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>> interfaces = ImplementedInterfaces(type, context.CancellationToken);
            foreach (string key in order)
            {
                List<int> positions = groups[key];
                if (AreTogether(members, positions, interfaces)) continue;

                MemberDeclarationSyntax first = members[positions[0]];
                if (!scope.IsAnalyzed(first.SyntaxTree)) continue;
                SyntaxToken name = Name(first);
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), name.ValueText));
            }
        }

        private static bool AreTogether(List<MemberDeclarationSyntax> members, List<int> positions, Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>> interfaces)
        {
            string name = OverloadName(members[positions[0]]);
            for (int k = 1; k < positions.Count; k++)
            {
                if (StartsOtherInterfaceSection(members, positions[k], positions[k - 1], interfaces)) continue;
                for (int i = positions[k - 1] + 1; i < positions[k]; i++)
                {
                    MemberDeclarationSyntax between = members[i];
                    if (between == null || OverloadName(between) != name) return false;
                }
            }
            return true;
        }

        // The overload implements a member of an interface the earlier one doesn't, and the member
        // next to it implements one of that interface too, so it sits in that interface's section.
        private static bool StartsOtherInterfaceSection(List<MemberDeclarationSyntax> members, int later, int earlier, Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>> interfaces)
        {
            if (!interfaces.TryGetValue(members[later], out HashSet<INamedTypeSymbol> own)) return false;
            if (interfaces.TryGetValue(members[earlier], out HashSet<INamedTypeSymbol> previous) && previous.Overlaps(own)) return false;
            return SharesInterface(members, later - 1, own, interfaces) || SharesInterface(members, later + 1, own, interfaces);
        }

        private static bool SharesInterface(List<MemberDeclarationSyntax> members, int index, HashSet<INamedTypeSymbol> own, Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>> interfaces)
        {
            if (index < 0 || index >= members.Count || members[index] == null) return false;
            return interfaces.TryGetValue(members[index], out HashSet<INamedTypeSymbol> theirs) && theirs.Overlaps(own);
        }

        // For each declaration of this type's members, the interfaces whose members it implements,
        // explicitly or not.
        private static Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>> ImplementedInterfaces(INamedTypeSymbol type, CancellationToken cancellationToken)
        {
            Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>> interfaces = new Dictionary<SyntaxNode, HashSet<INamedTypeSymbol>>();
            foreach (INamedTypeSymbol contract in type.AllInterfaces)
            {
                foreach (ISymbol member in contract.GetMembers())
                {
                    ISymbol implementation = type.FindImplementationForInterfaceMember(member);
                    if (implementation == null || !SymbolEqualityComparer.Default.Equals(implementation.ContainingType, type)) continue;
                    foreach (SyntaxReference reference in implementation.DeclaringSyntaxReferences)
                    {
                        // A field-like event's declarator sits inside its declaration.
                        SyntaxNode declaration = reference.GetSyntax(cancellationToken);
                        if (declaration is VariableDeclaratorSyntax) declaration = declaration.Parent?.Parent;
                        if (declaration == null) continue;
                        if (!interfaces.TryGetValue(declaration, out HashSet<INamedTypeSymbol> contracts)) interfaces[declaration] = contracts = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                        contracts.Add(contract);
                    }
                }
            }
            return interfaces;
        }

        // What makes members overloads of each other: the method name, or being a constructor.
        // Explicit interface implementations have none.
        private static string OverloadName(MemberDeclarationSyntax member)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method when method.ExplicitInterfaceSpecifier == null: return method.Identifier.ValueText;
                case ConstructorDeclarationSyntax _: return ".ctor";
                default: return null;
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

        // Accessibility, static and abstract, in a fixed order so "static public" matches "public
        // static". No accessibility modifier means the default: public in an interface, private
        // elsewhere.
        private static string Modifiers(MemberDeclarationSyntax member)
        {
            string key = "";
            foreach (string accessibility in AccessibilityModifiers)
            {
                if (HasModifier(member, accessibility)) key += " " + accessibility;
            }
            if (key.Length == 0) key = member.Parent is InterfaceDeclarationSyntax ? " public" : " private";
            if (HasModifier(member, "static")) key += " static";
            return HasModifier(member, "abstract") ? key + " abstract" : key;
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