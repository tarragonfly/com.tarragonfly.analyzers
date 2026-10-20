using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // How code uses a field, for TF1506 (never read) and TF2054 (never assigned).
    internal static class FieldUses
    {
        // Fields only code reads and writes: private ones, or any field of a private nested type; not
        // const, not compiler-made, without attributes ([SerializeField], [SerializeReference], Odin's
        // attributes and the like mean something outside the code sets or reads them), not in a
        // [Serializable] type, which a serializer fills, and not in a [StructLayout] type, whose
        // fields mirror memory that is read or written as a whole.
        public static bool IsCodeOnly(IFieldSymbol field)
        {
            if (!IsEffectivelyPrivate(field) || field.IsConst || field.IsImplicitlyDeclared) return false;
            if (!field.GetAttributes().IsEmpty || field.Locations.Length == 0 || !field.Locations[0].IsInSource) return false;
            if (SymbolNames.HasAttribute(field.ContainingType, "System.Runtime.InteropServices.StructLayoutAttribute")) return false;
            return !SymbolNames.HasAttribute(field.ContainingType, "System.SerializableAttribute");
        }

        // A plain write: the target of =, a deconstruction or an out argument. ++, += and ref
        // arguments also write, but read the old value first.
        public static bool IsPlainWrite(IFieldReferenceOperation reference)
        {
            switch (reference.Parent)
            {
                case ISimpleAssignmentOperation assignment: return assignment.Target == reference;
                case IArgumentOperation argument: return argument.Parameter?.RefKind == RefKind.Out;
                case ITupleOperation tuple: return tuple.Parent is IDeconstructionAssignmentOperation deconstruction && deconstruction.Target == tuple;
                default: return false;
            }
        }

        // A write whose value the code goes on to use, as in "a = b = 0" or "total = (count += 1)",
        // which reads the field as well. A void method or lambda written with "=>" uses nothing.
        public static bool IsWriteUsedAsValue(IFieldReferenceOperation reference)
        {
            if (reference.Parent is IArgumentOperation || reference.Parent is ITupleOperation) return false;
            IOperation write = reference.Parent;
            if (write == null || write.Parent is IExpressionStatementOperation) return false;
            SyntaxNode parent = write.Syntax.Parent;
            while (parent is ParenthesizedExpressionSyntax) parent = parent.Parent;
            return !(parent is ExpressionStatementSyntax) && !(parent is ForStatementSyntax);
        }

        public static bool IsReadModifyWrite(IFieldReferenceOperation reference)
        {
            switch (reference.Parent)
            {
                case ICompoundAssignmentOperation compound: return compound.Target == reference;
                case ICoalesceAssignmentOperation coalesce: return coalesce.Target == reference;
                case IIncrementOrDecrementOperation _: return true;
                case IArgumentOperation argument: return argument.Parameter?.RefKind == RefKind.Ref;
                default: return false;
            }
        }

        // Private itself, or reachable only through a private containing type.
        private static bool IsEffectivelyPrivate(ISymbol symbol)
        {
            if (symbol.DeclaredAccessibility == Accessibility.Private) return true;
            for (INamedTypeSymbol type = symbol.ContainingType; type != null; type = type.ContainingType)
            {
                if (type.DeclaredAccessibility == Accessibility.Private) return true;
            }
            return false;
        }
    }
}