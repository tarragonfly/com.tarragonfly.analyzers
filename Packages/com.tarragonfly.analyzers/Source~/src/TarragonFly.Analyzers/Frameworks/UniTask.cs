using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Frameworks
{
    // UniTask and the .NET task types it sits beside.
    public static class UniTask
    {
        private const string UniTaskNamespace = "Cysharp.Threading.Tasks";

        private static readonly string[] TaskTypes =
        {
            "Cysharp.Threading.Tasks.UniTask",
            "Cysharp.Threading.Tasks.UniTask`1",
            "System.Threading.Tasks.Task",
            "System.Threading.Tasks.Task`1",
            "System.Threading.Tasks.ValueTask",
            "System.Threading.Tasks.ValueTask`1"
        };

        // UniTask, Task or ValueTask, with or without a result. The caller awaits these or calls
        // Forget(). UniTaskVoid isn't on the list: no GetAwaiter(), it just runs to the end by itself.
        public static bool IsTask(ITypeSymbol type)
        {
            if (type == null) return false;
            foreach (string name in TaskTypes)
            {
                if (SymbolNames.IsNamed(type.OriginalDefinition, name)) return true;
            }
            return false;
        }

        // The return type of a fire-and-forget async method, UniTask's version of async void.
        public static bool IsUniTaskVoid(ITypeSymbol type)
        {
            return SymbolNames.IsNamed(type, "Cysharp.Threading.Tasks.UniTaskVoid");
        }

        public static bool IsCancellationToken(ITypeSymbol type)
        {
            return SymbolNames.IsNamed(type, "System.Threading.CancellationToken");
        }

        // task.Forget(): runs the task without awaiting it and logs its exception.
        public static bool IsForget(IMethodSymbol method)
        {
            return IsUniTaskMethod(method, "Forget");
        }

        // task.Preserve(): lets a UniTask be awaited more than once.
        public static bool IsPreserve(IMethodSymbol method)
        {
            return IsUniTaskMethod(method, "Preserve");
        }

        // task.ToCoroutine(): hands the task to StartCoroutine.
        public static bool IsToCoroutine(IMethodSymbol method)
        {
            return IsUniTaskMethod(method, "ToCoroutine");
        }

        // Instance and extension forms both live in the UniTask namespace.
        private static bool IsUniTaskMethod(IMethodSymbol method, string name)
        {
            if (method == null || method.Name != name) return false;
            IMethodSymbol original = method.ReducedFrom ?? method;
            return original.ContainingType != null && SymbolNames.IsInNamespace(original.ContainingType, UniTaskNamespace);
        }
    }
}