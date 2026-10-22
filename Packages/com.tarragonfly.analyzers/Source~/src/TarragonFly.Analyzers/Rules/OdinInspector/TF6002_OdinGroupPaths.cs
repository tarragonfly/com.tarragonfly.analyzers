using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.OdinInspector
{
    // TF6002, TF6003, TF6004: Odin group attributes whose paths don't add up. Odin only builds the
    // groups when it draws the Inspector, so these compile fine and turn up later as a missing field,
    // an error box or a console error once someone opens that object.
    //
    // TF6002, as in "[BoxGroup("Stats/Combat")]" when nothing declares a "Stats" group: a nested group
    // needs every group above it declared on the type or one of its base types.
    //
    // TF6003, as in "[FoldoutGroup("Row")]" on one field and "[HorizontalGroup("Row")]" on another:
    // Odin wants one group attribute type per group path and logs an error otherwise. The declaration
    // Odin meets first, base types first and then declaration order, sets the type.
    //
    // TF6004, as in "[BoxGroup("A/B"), BoxGroup("A/C")] int x;": a member can carry several group
    // attributes only when each group sits inside the next.
    //
    // The path comes from the constructor argument Odin builds the group ID from, with Odin's defaults
    // for the parameterless forms, and a tab is a group of its own at "group/tab". ColumnGroup and
    // custom group attributes with an unfamiliar constructor can't be read, so a type that has one
    // skips TF6002, since that attribute might declare the missing parent. Reported on the path string,
    // or on the member for TF6004. Only members of the analyzed type are reported; base types report
    // their own.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OdinGroupPathAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor UnknownParentGroup = Descriptors.Create(
            "TF6002",
            "Odin group nested in a group nothing declares",
            "'{0}' sits inside '{1}', but nothing on '{2}' declares a '{1}' group",
            "Odin",
            CodeKind.All);

        public static readonly DiagnosticDescriptor MixedGroupTypes = Descriptors.Create(
            "TF6003",
            "Odin group declared with different group attributes",
            "'{0}' is a {1} here but a {2} on '{3}'; use one group attribute type per group",
            "Odin",
            CodeKind.All);

        public static readonly DiagnosticDescriptor GroupsDontNest = Descriptors.Create(
            "TF6004",
            "Member placed in Odin groups that don't nest",
            "'{0}' is in both '{1}' and '{2}'; one of the groups has to sit inside the other",
            "Odin",
            CodeKind.All);

        private const string GroupAttributeBase = "Sirenix.OdinInspector.PropertyGroupAttribute";
        private const string TabGroupAttribute = "Sirenix.OdinInspector.TabGroupAttribute";

        // Constructor parameters that hold the group path, by the names Odin's own group attributes use.
        private static readonly string[] PathParameters = { "group", "groupName", "groupId", "path", "title", "toggleMemberName" };

        // The group ID Odin uses when these are given no path, such as "[HorizontalGroup(0.5f)]".
        private static readonly Dictionary<string, string> DefaultPaths = new Dictionary<string, string>
        {
            ["Sirenix.OdinInspector.BoxGroupAttribute"] = "_DefaultBoxGroup",
            ["Sirenix.OdinInspector.HorizontalGroupAttribute"] = "_DefaultHorizontalGroup",
            ["Sirenix.OdinInspector.VerticalGroupAttribute"] = "_DefaultVerticalGroup",
            [TabGroupAttribute] = "_DefaultTabGroup"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(UnknownParentGroup, MixedGroupTypes, GroupsDontNest);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                List<GroupAttribute> own = GroupAttributesOn(type);
                if (own.Count == 0) return;

                List<GroupAttribute> all = new List<GroupAttribute>();
                foreach (INamedTypeSymbol baseType in BaseTypesRootFirst(type)) all.AddRange(GroupAttributesOn(baseType));
                all.AddRange(own);

                void Report(DiagnosticDescriptor rule, Location location, params object[] arguments)
                {
                    if (!location.IsInSource || !scope.IsAnalyzed(location.SourceTree)) return;
                    context.ReportDiagnostic(Diagnostic.Create(rule, location, arguments));
                }

                ReportUnknownParents(type, own, all, context.CancellationToken, Report);
                ReportMixedTypes(own, all, context.CancellationToken, Report);
                ReportGroupsThatDontNest(own, Report);
            }, SymbolKind.NamedType);
        }

        private delegate void Reporter(DiagnosticDescriptor rule, Location location, params object[] arguments);

        private static void ReportUnknownParents(INamedTypeSymbol type, List<GroupAttribute> own, List<GroupAttribute> all, CancellationToken ct, Reporter report)
        {
            if (all.Any(group => group.Path == null)) return;

            HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (GroupAttribute group in all)
            {
                declared.Add(group.Path);
                declared.Add(group.MemberPath);
            }

            foreach (GroupAttribute group in own)
            {
                for (int slash = group.Path.IndexOf('/'); slash > 0; slash = group.Path.IndexOf('/', slash + 1))
                {
                    string parent = group.Path.Substring(0, slash);
                    if (declared.Contains(parent)) continue;
                    report(UnknownParentGroup, group.PathLocation(ct), group.Path, parent, type.Name);
                    break;
                }
            }
        }

        private static void ReportMixedTypes(List<GroupAttribute> own, List<GroupAttribute> all, CancellationToken ct, Reporter report)
        {
            Dictionary<string, GroupAttribute> firstByPath = new Dictionary<string, GroupAttribute>(StringComparer.Ordinal);
            foreach (GroupAttribute group in all)
            {
                if (group.Path == null) continue;
                if (!firstByPath.TryGetValue(group.Path, out GroupAttribute first))
                {
                    firstByPath.Add(group.Path, group);
                    continue;
                }

                INamedTypeSymbol kind = group.Attribute.AttributeClass;
                if (SymbolEqualityComparer.Default.Equals(kind, first.Attribute.AttributeClass) || !own.Contains(group)) continue;
                report(MixedGroupTypes, group.PathLocation(ct), group.Path, ShortName(kind), ShortName(first.Attribute.AttributeClass), first.Member.Name);
            }
        }

        private static void ReportGroupsThatDontNest(List<GroupAttribute> own, Reporter report)
        {
            foreach (IGrouping<ISymbol, GroupAttribute> member in own.GroupBy(group => group.Member, SymbolEqualityComparer.Default))
            {
                List<string> paths = member
                    .Where(group => group.MemberPath != null)
                    .Select(group => group.MemberPath)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();

                for (int i = 0; i + 1 < paths.Count; i++)
                {
                    if (paths[i + 1].StartsWith(paths[i] + "/", StringComparison.Ordinal)) continue;
                    report(GroupsDontNest, member.Key.Locations.FirstOrDefault() ?? Location.None, member.Key.Name, paths[i], paths[i + 1]);
                    break;
                }
            }
        }

        private static List<GroupAttribute> GroupAttributesOn(INamedTypeSymbol type)
        {
            List<GroupAttribute> found = new List<GroupAttribute>();
            foreach (ISymbol member in type.GetMembers())
            {
                if (member.IsImplicitlyDeclared || !(member is IFieldSymbol || member is IPropertySymbol || member is IMethodSymbol)) continue;
                foreach (AttributeData attribute in member.GetAttributes())
                {
                    if (attribute.AttributeClass == null || !SymbolNames.IsOrDerivesFrom(attribute.AttributeClass, GroupAttributeBase)) continue;
                    found.Add(GroupAttribute.Read(attribute, member));
                }
            }
            return found;
        }

        private static IEnumerable<INamedTypeSymbol> BaseTypesRootFirst(INamedTypeSymbol type)
        {
            Stack<INamedTypeSymbol> baseTypes = new Stack<INamedTypeSymbol>();
            for (INamedTypeSymbol current = type.BaseType; current != null; current = current.BaseType) baseTypes.Push(current);
            return baseTypes;
        }

        // "HorizontalGroup" for HorizontalGroupAttribute.
        private static string ShortName(INamedTypeSymbol attributeClass)
        {
            string name = attributeClass.Name;
            return name.EndsWith("Attribute", StringComparison.Ordinal) ? name.Substring(0, name.Length - "Attribute".Length) : name;
        }

        // One group attribute on one member. Path is the group it declares ("Tabs" for a TabGroup) and
        // MemberPath the group the member lands in ("Tabs/Stats"); both are null when the path can't be read.
        private sealed class GroupAttribute
        {
            private GroupAttribute(AttributeData attribute, ISymbol member, string path, string memberPath, string pathParameter)
            {
                Attribute = attribute;
                Member = member;
                Path = path;
                MemberPath = memberPath;
                PathParameter = pathParameter;
            }

            public AttributeData Attribute { get; }
            public ISymbol Member { get; }
            public string Path { get; }
            public string MemberPath { get; }

            // The constructor parameter the path came from, or null for a default path.
            private string PathParameter { get; }

            public static GroupAttribute Read(AttributeData attribute, ISymbol member)
            {
                string attributeName = attribute.AttributeClass.ToDisplayString();
                string groupIdOverride = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "GroupID").Value.Value as string;

                if (attributeName == TabGroupAttribute)
                {
                    string tab = Argument(attribute, "tab");
                    string group = groupIdOverride ?? Argument(attribute, "group");
                    string groupParameter = group != null && groupIdOverride == null ? "group" : null;
                    group = group ?? DefaultPaths[TabGroupAttribute];
                    return new GroupAttribute(attribute, member, group, tab == null ? group : group + "/" + tab, groupParameter);
                }

                if (groupIdOverride != null) return new GroupAttribute(attribute, member, groupIdOverride, groupIdOverride, null);

                foreach (string parameter in PathParameters)
                {
                    string path = Argument(attribute, parameter);
                    if (path != null) return new GroupAttribute(attribute, member, path, path, parameter);
                }

                return DefaultPaths.TryGetValue(attributeName, out string defaultPath)
                    ? new GroupAttribute(attribute, member, defaultPath, defaultPath, null)
                    : new GroupAttribute(attribute, member, null, null, null);
            }

            // The path string in the attribute, or the whole attribute when the path is a default.
            public Location PathLocation(CancellationToken ct)
            {
                if (!(Attribute.ApplicationSyntaxReference?.GetSyntax(ct) is AttributeSyntax syntax)) return Location.None;
                if (PathParameter == null || syntax.ArgumentList == null) return syntax.GetLocation();

                SeparatedSyntaxList<AttributeArgumentSyntax> arguments = syntax.ArgumentList.Arguments;
                AttributeArgumentSyntax byName = arguments.FirstOrDefault(argument => argument.NameColon?.Name.Identifier.ValueText == PathParameter);
                if (byName != null) return byName.Expression.GetLocation();

                int position = Attribute.AttributeConstructor.Parameters.IndexOf(Attribute.AttributeConstructor.Parameters.First(parameter => parameter.Name == PathParameter));
                List<AttributeArgumentSyntax> positional = arguments.Where(argument => argument.NameColon == null && argument.NameEquals == null).ToList();
                return position < positional.Count ? positional[position].Expression.GetLocation() : syntax.GetLocation();
            }

            // A string constructor argument by parameter name, defaults included.
            private static string Argument(AttributeData attribute, string parameterName)
            {
                IMethodSymbol constructor = attribute.AttributeConstructor;
                if (constructor == null) return null;
                for (int i = 0; i < constructor.Parameters.Length && i < attribute.ConstructorArguments.Length; i++)
                {
                    if (constructor.Parameters[i].Name == parameterName) return attribute.ConstructorArguments[i].Value as string;
                }
                return null;
            }
        }
    }
}