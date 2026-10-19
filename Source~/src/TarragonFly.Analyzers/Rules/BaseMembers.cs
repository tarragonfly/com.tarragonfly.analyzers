using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Rules
{
    // The declarations a member's signature comes from: the member it overrides and the interface
    // members it implements, explicitly or not.
    public static class BaseMembers
    {
        public static List<IMethodSymbol> Of(IMethodSymbol method)
        {
            List<IMethodSymbol> bases = new List<IMethodSymbol>();
            if (method.OverriddenMethod != null) bases.Add(method.OverriddenMethod);
            bases.AddRange(method.ExplicitInterfaceImplementations);

            INamedTypeSymbol type = method.ContainingType;
            if (type == null || method.MethodKind != MethodKind.Ordinary) return bases;
            foreach (INamedTypeSymbol candidate in type.AllInterfaces)
            {
                foreach (ISymbol member in candidate.GetMembers(method.Name))
                {
                    if (!(member is IMethodSymbol interfaceMethod)) continue;
                    if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(interfaceMethod), method) && !bases.Contains(interfaceMethod))
                    {
                        bases.Add(interfaceMethod);
                    }
                }
            }
            return bases;
        }

        // Its signature is set by a base class or interface, so it can't be changed here alone.
        public static bool IsOverrideOrImplementation(IMethodSymbol method)
        {
            return method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0 || Of(method).Count > 0;
        }

        public static bool IsOverrideOrImplementation(IPropertySymbol property)
        {
            if (property.IsOverride || property.ExplicitInterfaceImplementations.Length > 0) return true;
            INamedTypeSymbol type = property.ContainingType;
            if (type == null) return false;
            foreach (INamedTypeSymbol candidate in type.AllInterfaces)
            {
                foreach (ISymbol member in candidate.GetMembers(property.Name))
                {
                    if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), property)) return true;
                }
            }
            return false;
        }
    }
}