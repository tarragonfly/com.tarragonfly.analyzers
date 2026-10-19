using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Frameworks
{
    // Odin Inspector: its serialized base types and the attributes that name members in strings.
    public static class Odin
    {
        private const string OdinNamespace = "Sirenix.OdinInspector";

        private static readonly string[] SerializedBaseTypes =
        {
            "Sirenix.OdinInspector.SerializedMonoBehaviour",
            "Sirenix.OdinInspector.SerializedScriptableObject",
            "Sirenix.OdinInspector.SerializedComponent",
            "Sirenix.OdinInspector.SerializedBehaviour",
            "Sirenix.OdinInspector.SerializedStateMachineBehaviour"
        };

        // Attributes whose first constructor argument is a member name or an "@" expression.
        private static readonly HashSet<string> MemberNameInFirstArgument = new HashSet<string>
        {
            "ShowIfAttribute", "HideIfAttribute", "EnableIfAttribute", "DisableIfAttribute",
            "ValueDropdownAttribute", "ValidateInputAttribute", "OnValueChangedAttribute", "InlineButtonAttribute"
        };

        // Odin saves fields of these types that Unity cannot, such as dictionaries and interfaces.
        public static bool IsOdinSerializedType(ITypeSymbol type)
        {
            foreach (string name in SerializedBaseTypes)
            {
                if (SymbolNames.IsOrDerivesFrom(type, name)) return true;
            }
            return false;
        }

        // A member an Odin attribute names in one of its string arguments.
        public readonly struct MemberReference
        {
            public MemberReference(string member, int position, string namedArgument, bool namesMemberDirectly)
            {
                Member = member;
                Position = position;
                NamedArgument = namedArgument;
                NamesMemberDirectly = namesMemberDirectly;
            }

            public string Member { get; }

            // The constructor argument's index, or -1 for a named argument such as VisibleIf.
            public int Position { get; }
            public string NamedArgument { get; }

            // True for an argument that is a member name, such as ShowIf's condition; false for "$Name"
            // inside text, such as a label.
            public bool NamesMemberDirectly { get; }
        }

        // Members an Odin attribute calls or reads by name, so they count as used. "$Name" in any string
        // argument is a member reference; "@..." expressions are C# snippets and are skipped.
        public static IEnumerable<string> GetReferencedMemberNames(AttributeData attribute)
        {
            foreach (MemberReference reference in GetMemberReferences(attribute)) yield return reference.Member;
        }

        public static IEnumerable<MemberReference> GetMemberReferences(AttributeData attribute)
        {
            INamedTypeSymbol type = attribute.AttributeClass;
            if (type == null || !SymbolNames.IsInNamespace(type, OdinNamespace)) yield break;

            for (int i = 0; i < attribute.ConstructorArguments.Length; i++)
            {
                if (attribute.ConstructorArguments[i].Kind == TypedConstantKind.Array || !(attribute.ConstructorArguments[i].Value is string text)) continue;
                bool namesMember = (i == 0 && MemberNameInFirstArgument.Contains(type.Name))
                    || (type.Name == "InfoBoxAttribute" && IsVisibleIfParameter(attribute, i));
                string member = ReadMemberName(text, allowPlainName: namesMember);
                if (member != null) yield return new MemberReference(member, i, null, namesMember && text[0] != '$');
            }

            foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
            {
                if (named.Value.Kind == TypedConstantKind.Array || !(named.Value.Value is string text)) continue;
                bool namesMember = named.Key == "VisibleIf" || named.Key == "Condition";
                string member = ReadMemberName(text, allowPlainName: namesMember);
                if (member != null) yield return new MemberReference(member, -1, named.Key, namesMember && text[0] != '$');
            }
        }

        // Both InfoBox constructors name the member that decides when the box shows
        // "visibleIfMemberName": second in one, third in the other.
        private static bool IsVisibleIfParameter(AttributeData attribute, int position)
        {
            IMethodSymbol constructor = attribute.AttributeConstructor;
            if (constructor == null) return position == 1;
            return position < constructor.Parameters.Length && constructor.Parameters[position].Name == "visibleIfMemberName";
        }

        // What the named member has to give Odin: a bool for conditions and ValidateInput (a bool field
        // or property, or a method returning bool), a collection for ValueDropdown, or anything.
        public enum MemberValue
        {
            Any,
            Bool,
            Collection
        }

        public static MemberValue RequiredMemberValue(AttributeData attribute, MemberReference reference)
        {
            if (!reference.NamesMemberDirectly) return MemberValue.Any;
            switch (attribute.AttributeClass?.Name)
            {
                case "ValidateInputAttribute":
                case "InfoBoxAttribute":
                    return MemberValue.Bool;
                case "ValueDropdownAttribute":
                    return MemberValue.Collection;
                case "ShowIfAttribute":
                case "HideIfAttribute":
                case "EnableIfAttribute":
                case "DisableIfAttribute":
                    // ShowIf("bossPhase", 2) compares the member with a value, so it needn't be a bool.
                    return ComparesWithValue(attribute) ? MemberValue.Any : MemberValue.Bool;
                default:
                    return MemberValue.Any;
            }
        }

        private static bool ComparesWithValue(AttributeData attribute)
        {
            IMethodSymbol constructor = attribute.AttributeConstructor;
            if (constructor == null) return false;
            foreach (IParameterSymbol parameter in constructor.Parameters)
            {
                if (parameter.Name == "optionalValue") return true;
            }
            return false;
        }

        // "Name" (when the argument is a member name) or "$Name", optionally followed by ".Path";
        // returns null for "@expressions" and plain text.
        private static string ReadMemberName(string text, bool allowPlainName)
        {
            if (string.IsNullOrEmpty(text) || text[0] == '@') return null;
            int start = text[0] == '$' ? 1 : 0;
            if (start == 0 && !allowPlainName) return null;

            int end = start;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            if (end == start || char.IsDigit(text[start])) return null;
            if (start == 0 && end != text.Length) return null;
            return text.Substring(start, end - start);
        }
    }
}