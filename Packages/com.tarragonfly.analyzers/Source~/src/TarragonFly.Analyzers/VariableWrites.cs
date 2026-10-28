using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers
{
    internal static class VariableWrites
    {
        // The reference is written here: the target of an assignment or deconstruction, through
        // tuples, conversions and parentheses, ++ or --, a ref or out argument, or "ref x", which
        // can write through the alias later.
        public static bool IsWrite(IOperation reference)
        {
            if (reference == null) return false;
            if (reference.Syntax.Parent is RefExpressionSyntax) return true;
            IOperation target = reference;
            while (target.Parent is ITupleOperation or IConversionOperation or IParenthesizedOperation) target = target.Parent;
            switch (target.Parent)
            {
                case IAssignmentOperation assignment: return assignment.Target == target;
                case IIncrementOrDecrementOperation _: return true;
                case IArgumentOperation argument: return argument.Parameter?.RefKind == RefKind.Ref || argument.Parameter?.RefKind == RefKind.Out;
                default: return false;
            }
        }
    }
}
