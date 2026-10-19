using System;
using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // Base class fields a derived field can clash with (TF2503, TF2510): instance fields the derived
    // class can see.
    public static class BaseFields
    {
        public static IFieldSymbol Find(IFieldSymbol field, StringComparison comparison)
        {
            for (INamedTypeSymbol type = field.ContainingType.BaseType; type != null; type = type.BaseType)
            {
                foreach (ISymbol member in type.GetMembers())
                {
                    if (member is IFieldSymbol baseField && !baseField.IsStatic && !baseField.IsImplicitlyDeclared
                        && baseField.DeclaredAccessibility != Accessibility.Private && string.Equals(baseField.Name, field.Name, comparison))
                    {
                        return baseField;
                    }
                }
            }
            return null;
        }
    }
}