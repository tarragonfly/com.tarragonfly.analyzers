using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Frameworks
{
    // What rules need to know about UnityEngine and UnityEditor. The table of Unity messages (Awake,
    // Update, OnValidate...) is the one forked from Microsoft.Unity.Analyzers in Rules/Unity/ScriptInfo.cs.
    public static class Unity
    {
        private static readonly string[] ImplicitUseAttributes =
        {
            "UnityEngine.Scripting.PreserveAttribute",
            "UnityEngine.ContextMenu",
            "UnityEngine.RuntimeInitializeOnLoadMethodAttribute",
            "UnityEditor.MenuItem",
            "UnityEditor.InitializeOnLoadMethodAttribute",
            "UnityEditor.InitializeOnLoadAttribute",
            "JetBrains.Annotations.UsedImplicitlyAttribute"
        };

        // Its == and != also check whether the native object was destroyed.
        public static bool IsUnityObject(ITypeSymbol type)
        {
            return SymbolNames.IsOrDerivesFrom(type, "UnityEngine.Object");
        }

        public static bool IsMonoBehaviour(ITypeSymbol type)
        {
            return SymbolNames.IsOrDerivesFrom(type, "UnityEngine.MonoBehaviour");
        }

        public static bool IsScriptableObject(ITypeSymbol type)
        {
            return SymbolNames.IsOrDerivesFrom(type, "UnityEngine.ScriptableObject");
        }

        // Whether Unity saves this field along with the object that holds it. For now this only reads
        // the field's own declaration, not whether Unity can save its type; that check comes with the
        // first rule that needs it. Auto-properties count through [field: SerializeField], which
        // Roslyn puts on the backing field.
        public static bool IsSavedByUnity(IFieldSymbol field)
        {
            if (field.IsStatic || field.IsConst || field.IsReadOnly) return false;
            if (SymbolNames.HasAttribute(field, "System.NonSerializedAttribute")) return false;
            if (field.DeclaredAccessibility == Accessibility.Public) return true;
            return SymbolNames.HasAttribute(field, "UnityEngine.SerializeField")
                || SymbolNames.HasAttribute(field, "UnityEngine.SerializeReference");
        }

        // Members that Unity, the editor or JetBrains annotations mark as used without a code reference.
        public static bool HasImplicitUseAttribute(ISymbol symbol)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                foreach (string name in ImplicitUseAttributes)
                {
                    if (SymbolNames.IsOrDerivesFrom(attribute.AttributeClass, name)) return true;
                }
                // JetBrains' [MeansImplicitUse] on an attribute class makes everything it marks used.
                if (attribute.AttributeClass != null && SymbolNames.HasAttribute(attribute.AttributeClass, "JetBrains.Annotations.MeansImplicitUseAttribute")) return true;
            }
            return false;
        }

        // A method Unity calls by name, such as Start on a MonoBehaviour or OnGUI on an EditorWindow.
        // Only the name and parameter count are compared, so "async UniTaskVoid Start()" counts too.
        public static bool IsMessage(IMethodSymbol method)
        {
            if (method.IsStatic || method.ContainingType == null) return false;
            foreach (System.Reflection.MethodInfo message in new Rules.Unity.ScriptInfo(method.ContainingType).GetMessages())
            {
                if (message.Name == method.Name && message.GetParameters().Length == method.Parameters.Length) return true;
            }
            return false;
        }

        // [ContextMenuItem("Label", "MethodName")] on a field calls MethodName from the Inspector.
        public static IEnumerable<string> GetMethodNamesReferencedBy(AttributeData attribute)
        {
            if (!SymbolNames.IsNamed(attribute.AttributeClass, "UnityEngine.ContextMenuItemAttribute")) yield break;
            if (attribute.ConstructorArguments.Length > 1 && attribute.ConstructorArguments[1].Value is string method) yield return method;
        }
    }
}