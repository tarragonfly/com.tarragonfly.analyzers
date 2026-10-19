using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Frameworks
{
    // Init(args) by Sisus. Its own analyzers (INIT0001-0008) already check [Service] and [InitAfter]
    // mistakes; TF rules use these checks to avoid false reports on Init(args) code.
    public static class InitArgs
    {
        private const string InitNamespace = "Sisus.Init";

        // [Service] arguments that point at an existing instance instead of creating one.
        private static readonly string[] InstanceSources =
        {
            "ResourcePath", "AddressableKey", "FindFromScene", "LoadScene", "SceneName", "SceneBuildIndex"
        };

        // MonoBehaviour<T...>, ScriptableObject<T...>, StateMachineBehaviour<T...> and custom clients
        // all implement IArgs<T...>.
        public static bool IsClient(ITypeSymbol type)
        {
            return type != null && SymbolNames.Implements(type, IsArgsInterface);
        }

        // The Init(...) method that receives a client's arguments; Init(args) calls it, not your code.
        public static bool IsInitMethod(IMethodSymbol method)
        {
            if (method == null || method.IsStatic || method.Name != "Init" || method.ContainingType == null) return false;
            foreach (INamedTypeSymbol candidate in method.ContainingType.AllInterfaces)
            {
                if (IsArgsInterface(candidate) && ParametersMatch(method, candidate)) return true;
            }
            return false;
        }

        // Any<T>: a serialized field that holds either a value or a service reference.
        public static bool IsAny(ITypeSymbol type)
        {
            return SymbolNames.IsGenericNamed(type, InitNamespace, "Any");
        }

        public static bool IsServiceAttribute(AttributeData attribute)
        {
            return SymbolNames.IsOrDerivesFrom(attribute.AttributeClass, "Sisus.Init.ServiceAttribute");
        }

        // The value a [Service] sets for a named argument such as LoadScene, or null when it isn't set.
        public static object GetServiceArgument(AttributeData attribute, string name)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == name && argument.Value.Kind != TypedConstantKind.Array) return argument.Value.Value;
            }
            return null;
        }

        // [Service(LoadScene = "00_Bootstrap")] or [Service(FindFromScene = true)]: the instance is a
        // component in a scene.
        public static bool IsSceneService(AttributeData attribute)
        {
            object findFromScene = GetServiceArgument(attribute, "FindFromScene");
            return GetServiceArgument(attribute, "LoadScene") != null || (findFromScene != null && !(findFromScene is false));
        }

        // A [Service] that says nowhere to get the instance from, so Init(args) creates a new one.
        public static bool CreatesNewInstance(AttributeData attribute)
        {
            foreach (string source in InstanceSources)
            {
                object value = GetServiceArgument(attribute, source);
                if (value != null && !(value is false) && !(value is string text && text.Length == 0) && !(value is int index && index < 0)) return false;
            }
            return true;
        }

        // Service and Service<T>: static access to registered services.
        public static bool IsServiceAccess(ITypeSymbol type)
        {
            return SymbolNames.IsNamed(type, "Sisus.Init.Service") || SymbolNames.IsGenericNamed(type, InitNamespace, "Service");
        }

        private static bool IsArgsInterface(INamedTypeSymbol type)
        {
            return SymbolNames.IsGenericNamed(type, InitNamespace, "IArgs");
        }

        private static bool ParametersMatch(IMethodSymbol method, INamedTypeSymbol argsInterface)
        {
            if (method.Parameters.Length != argsInterface.TypeArguments.Length) return false;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(method.Parameters[i].Type, argsInterface.TypeArguments[i])) return false;
            }
            return true;
        }
    }
}