using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers
{
    internal static class LocalAssignments
    {
        public static IOperation ResolveValue(SemanticModel model, IOperation value, CancellationToken ct)
        {
            HashSet<ILocalSymbol> visited = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            while (true)
            {
                if (value is IConversionOperation conversion) value = conversion.Operand;
                else if (value is IParenthesizedOperation parenthesized) value = parenthesized.Operand;
                else if (value is ILocalReferenceOperation local)
                {
                    if (!visited.Add(local.Local)) return null;
                    value = FindValueBefore(model, local.Local, local.Syntax.SpanStart, ct);
                }
                else return value;
            }
        }

        // The lambdas and method groups a delegate value may hold. A local whose value is unknown,
        // because it was set in a branch or with +=, may hold any value it was given in its block.
        public static IEnumerable<IOperation> PossibleDelegates(SemanticModel model, IOperation value, CancellationToken ct)
        {
            bool hasDelegateType = value?.Type?.TypeKind == TypeKind.Delegate;
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (!hasDelegateType && value?.Type?.TypeKind != TypeKind.Delegate && value is not IAnonymousFunctionOperation && value is not IMethodReferenceOperation) yield break;
            IOperation resolved = ResolveValue(model, value, ct);
            IEnumerable<IOperation> candidates = resolved != null ? new[] { resolved }
                : value is ILocalReferenceOperation local ? PossibleValues(model, local.Local, ct).Select(possible => ResolveValue(model, possible, ct))
                : Enumerable.Empty<IOperation>();
            foreach (IOperation candidate in candidates)
            {
                IOperation target = candidate is IDelegateCreationOperation creation ? creation.Target : candidate;
                if (target is IAnonymousFunctionOperation || target is IMethodReferenceOperation) yield return target;
            }
        }

        // The delegate a call invokes: "check" in check(), check.Invoke() and check?.Invoke().
        public static IOperation InvokedDelegate(IInvocationOperation call)
        {
            if (!(call.Instance is IConditionalAccessInstanceOperation)) return call.Instance;
            for (IOperation parent = call.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is IConditionalAccessOperation access) return access.Operation;
            }
            return null;
        }

        // Only straight-line writes are followed. Branching or aliased writes leave the value unknown.
        private static IOperation FindValueBefore(SemanticModel model, ILocalSymbol local, int position, CancellationToken ct)
        {
            if (!TryGetDeclaringBlock(local, model, ct, out VariableDeclaratorSyntax declaration, out BlockSyntax block)) return null;
            ExpressionSyntax value = declaration.Initializer?.Value;
            if (value?.Span.End > position) value = null;
            foreach ((IdentifierNameSyntax name, IOperation reference) in References(model, local, block, ct))
            {
                if (name.SpanStart >= position) break;
                IOperation target = reference;
                while (target?.Parent is ITupleOperation or IConversionOperation or IParenthesizedOperation) target = target.Parent;
                if (target?.Parent is IAssignmentOperation assignment && assignment.Target == target)
                {
                    bool straightLine = assignment.Syntax.Span.End <= position && assignment.Syntax.Parent is ExpressionStatementSyntax statement && statement.Parent == block;
                    value = straightLine && (assignment is ISimpleAssignmentOperation simple && !simple.IsRef && target == reference || assignment is IDeconstructionAssignmentOperation)
                        ? AssignedValue(reference, assignment)?.Syntax as ExpressionSyntax : null;
                }
                else if (VariableWrites.IsWrite(reference)) value = null;
            }
            return value == null ? null : model.GetOperation(value, ct);
        }

        private static List<IOperation> PossibleValues(SemanticModel model, ILocalSymbol local, CancellationToken ct)
        {
            List<IOperation> values = new List<IOperation>();
            if (!TryGetDeclaringBlock(local, model, ct, out VariableDeclaratorSyntax declaration, out BlockSyntax block)) return values;
            if (declaration.Initializer != null && model.GetOperation(declaration.Initializer.Value, ct) is IOperation initial) values.Add(initial);
            foreach ((IdentifierNameSyntax _, IOperation reference) in References(model, local, block, ct))
            {
                IOperation target = reference;
                while (target?.Parent is ITupleOperation or IConversionOperation or IParenthesizedOperation) target = target.Parent;
                if (target?.Parent is IAssignmentOperation assignment && assignment.Target == target && AssignedValue(reference, assignment) is IOperation assigned) values.Add(assigned);
            }
            return values;
        }

        // The value written to this reference: the right side, or its own element of a tuple.
        private static IOperation AssignedValue(IOperation reference, IAssignmentOperation assignment)
        {
            if (assignment.Target == reference) return assignment.Value;
            if (!(reference.Parent is ITupleOperation targets) || assignment.Target != targets) return null;
            IOperation value = assignment.Value;
            while (value is IConversionOperation conversion) value = conversion.Operand;
            int index = targets.Elements.IndexOf(reference);
            return value is ITupleOperation values && values.Elements.Length == targets.Elements.Length ? values.Elements[index] : null;
        }

        private static bool TryGetDeclaringBlock(ILocalSymbol local, SemanticModel model, CancellationToken ct, out VariableDeclaratorSyntax declaration, out BlockSyntax block)
        {
            declaration = null;
            block = null;
            if (local.DeclaringSyntaxReferences.Length != 1 || local.DeclaringSyntaxReferences[0].SyntaxTree != model.SyntaxTree) return false;
            declaration = local.DeclaringSyntaxReferences[0].GetSyntax(ct) as VariableDeclaratorSyntax;
            block = declaration?.FirstAncestorOrSelf<BlockSyntax>();
            return block != null;
        }

        private static IEnumerable<(IdentifierNameSyntax, IOperation)> References(SemanticModel model, ILocalSymbol local, BlockSyntax block, CancellationToken ct)
        {
            foreach (IdentifierNameSyntax name in block.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (name.Identifier.ValueText != local.Name || !SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, ct).Symbol, local)) continue;
                yield return (name, model.GetOperation(name, ct));
            }
        }
    }
}
