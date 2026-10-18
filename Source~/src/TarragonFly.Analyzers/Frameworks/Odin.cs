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

        // Members an Odin attribute calls or reads by name, so they count as used. "$Name" in any string
        // argument is a member reference; "@..." expressions are C# snippets and are skipped.
        public static IEnumerable<string> GetReferencedMemberNames(AttributeData attribute)
        {
            INamedTypeSymbol type = attribute.AttributeClass;
            if (type == null || !SymbolNames.IsInNamespace(type, OdinNamespace)) yield break;

            for (int i = 0; i < attribute.ConstructorArguments.Length; i++)
            {
                if (!(attribute.ConstructorArguments[i].Value is string text)) continue;
                bool namesMember = (i == 0 && MemberNameInFirstArgument.Contains(type.Name))
                    || (i == 1 && type.Name == "InfoBoxAttribute");
                string member = namesMember ? ReadMemberName(text, allowPlainName: true) : ReadMemberName(text, allowPlainName: false);
                if (member != null) yield return member;
            }

            foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
            {
                if (!(named.Value.Value is string text)) continue;
                bool namesMember = named.Key == "VisibleIf" || named.Key == "Condition";
                string member = ReadMemberName(text, allowPlainName: namesMember);
                if (member != null) yield return member;
            }
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