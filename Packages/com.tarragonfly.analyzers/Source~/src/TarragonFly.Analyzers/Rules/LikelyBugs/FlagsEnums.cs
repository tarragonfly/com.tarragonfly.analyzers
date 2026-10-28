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
            switch (value)
            {
                case sbyte signed: return unchecked((byte)signed);
                case short signed: return unchecked((ushort)signed);
                case int signed: return unchecked((uint)signed);
                case long signed: return unchecked((ulong)signed);
                case byte unsigned: return unsigned;
                case ushort unsigned: return unsigned;
                case uint unsigned: return unsigned;
                case ulong unsigned: return unsigned;
                default: return null;
            }
        }

        public static ulong AllBits(INamedTypeSymbol type)
        {
            switch (type.EnumUnderlyingType.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte: return byte.MaxValue;
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16: return ushort.MaxValue;
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32: return uint.MaxValue;
                default: return ulong.MaxValue;
            }
        }

        public static bool IsSingleBit(ulong value)
        {
            return value != 0 && (value & (value - 1)) == 0;
        }
    }
}
