using System;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Frameworks
{
    // Matches symbols by full name rather than by resolved type, so the checks work whether the
    // framework is a DLL, a package's source or a test stub, and when a project defines a type twice.
    public static class SymbolNames
    {
        // fullMetadataName uses metadata names: "UnityEngine.Object", "Cysharp.Threading.Tasks.UniTask`1".
        public static bool IsNamed(ISymbol symbol, string fullMetadataName)
        {
            if (symbol == null) return false;
            int dot = fullMetadataName.LastIndexOf('.');
            string name = dot < 0 ? fullMetadataName : fullMetadataName.Substring(dot + 1);
            if (symbol.MetadataName != name) return false;
            return IsInNamespace(symbol, dot < 0 ? string.Empty : fullMetadataName.Substring(0, dot));
        }

        // Generic families such as Sisus.Init.IArgs<T1..T12>: any arity above zero.
        public static bool IsGenericNamed(ITypeSymbol type, string namespaceName, string name)
        {
            return type is INamedTypeSymbol named && named.Arity > 0 && named.Name == name && IsInNamespace(named, namespaceName);
        }

        public static bool IsInNamespace(ISymbol symbol, string namespaceName)
        {
            if (symbol.ContainingType != null) return false;
            INamespaceSymbol current = symbol.ContainingNamespace;
            int end = namespaceName.Length;
            while (end > 0)
            {
                if (current == null || current.IsGlobalNamespace) return false;
                int start = namespaceName.LastIndexOf('.', end - 1) + 1;
                if (string.CompareOrdinal(current.Name, 0, namespaceName, start, end - start) != 0 || current.Name.Length != end - start) return false;
                current = current.ContainingNamespace;
                end = start - 1;
            }
            return current == null || current.IsGlobalNamespace;
        }

        public static bool IsOrDerivesFrom(ITypeSymbol type, string fullMetadataName)
        {
            for (ITypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (IsNamed(current.OriginalDefinition, fullMetadataName)) return true;
            }
            return false;
        }

        public static bool Implements(ITypeSymbol type, Func<INamedTypeSymbol, bool> isInterface)
        {
            if (type is INamedTypeSymbol named && named.TypeKind == TypeKind.Interface && isInterface(named)) return true;
            foreach (INamedTypeSymbol candidate in type.AllInterfaces)
            {
                if (isInterface(candidate)) return true;
            }
            return false;
        }

        // Counts attributes derived from the named one, such as Init(args)' [Service] from [Preserve].
        public static bool HasAttribute(ISymbol symbol, string attributeFullMetadataName)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (IsOrDerivesFrom(attribute.AttributeClass, attributeFullMetadataName)) return true;
            }
            return false;
        }
    }
}