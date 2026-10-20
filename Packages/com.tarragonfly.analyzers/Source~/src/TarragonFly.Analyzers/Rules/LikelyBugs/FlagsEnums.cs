using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // What the [Flags] enum rules (TF2027, TF2028, TF2063) need: whether an enum is one, and member values.
    public static class FlagsEnums
    {
        public static bool IsFlags(INamedTypeSymbol type)
        {
            return type != null && type.TypeKind == TypeKind.Enum && SymbolNames.HasAttribute(type, "System.FlagsAttribute");
        }

        // The member's value as a bit pattern; null if the compiler couldn't work it out.
        public static ulong? Value(SemanticModel model, EnumMemberDeclarationSyntax member)
        {
            if (!(model.GetDeclaredSymbol(member) is IFieldSymbol field) || !field.HasConstantValue || field.ConstantValue == null) return null;
            object value = field.ConstantValue;
            return value is ulong unsigned ? unsigned : unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        public static bool IsSingleBit(ulong value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }
    }
}