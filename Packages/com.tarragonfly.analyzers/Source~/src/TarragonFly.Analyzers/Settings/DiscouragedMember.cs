using System;

namespace TarragonFly.Analyzers.Settings
{
    // One entry of discouraged_members: a member the project wants called through its own helper,
    // as in "UnityEngine.UIElements.UQueryExtensions.Q -> UIViewBase.Element". With a parameter list
    // after the member name, "UnityEngine.Debug.Log(object, UnityEngine.Object) -> Log.Info", only
    // that overload counts. TF1029 reads these.
    public sealed class DiscouragedMember
    {
        private DiscouragedMember(string typeName, string memberName, string parameterTypes, string replacement)
        {
            TypeName = typeName;
            MemberName = memberName;
            ParameterTypes = parameterTypes;
            Replacement = replacement;
        }

        // The containing type's full name, generic arguments left out: "System.Collections.Generic.List".
        public string TypeName { get; }
        public string MemberName { get; }

        // The overload's parameter types as written between the parentheses, comma separated with
        // one space after each comma, or null when every overload counts.
        public string ParameterTypes { get; }
        public string Replacement { get; }

        // Returns an error message, or null when the entry was parsed.
        public static string TryParse(string entry, out DiscouragedMember member)
        {
            member = null;
            int arrow = entry.IndexOf("->", StringComparison.Ordinal);
            if (arrow < 0) return $"'{entry}' needs the form 'Namespace.Type.Member -> Replacement'.";
            string call = entry.Substring(0, arrow).Trim();
            string replacement = entry.Substring(arrow + 2).Trim();
            if (replacement.Length == 0) return $"'{entry}' names nothing to call instead; write 'Namespace.Type.Member -> Replacement'.";

            string parameterTypes = null;
            int open = call.IndexOf('(');
            if (open >= 0)
            {
                if (!call.EndsWith(")", StringComparison.Ordinal)) return $"'{entry}' has an unclosed parameter list.";
                parameterTypes = NormalizeParameterTypes(call.Substring(open + 1, call.Length - open - 2));
                call = call.Substring(0, open).Trim();
            }

            int lastDot = call.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == call.Length - 1) return $"'{entry}' needs a type and a member name, as in 'Namespace.Type.Member -> Replacement'.";
            member = new DiscouragedMember(StripTypeArguments(call.Substring(0, lastDot)), call.Substring(lastDot + 1), parameterTypes, replacement);
            return null;
        }

        // "List<T>" and "List" name the same type here; the overload's parameters tell them apart.
        public static string StripTypeArguments(string typeName)
        {
            int open = typeName.IndexOf('<');
            return open < 0 ? typeName : typeName.Substring(0, open);
        }

        // "VisualElement,string , string" and "VisualElement, string, string" are the same list.
        public static string NormalizeParameterTypes(string parameterTypes)
        {
            string[] types = parameterTypes.Split(',');
            for (int i = 0; i < types.Length; i++) types[i] = types[i].Trim();
            return string.Join(", ", types);
        }
    }
}
