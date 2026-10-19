using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // Writes to static fields inside one member, for TF7002 and TF7004.
    internal static class StaticFieldWrites
    {
        // The first write to each static field in the code, lambdas and local functions included:
        // assignments ("count = 1", "count += 1"), reported from the field to the operator, and
        // ++ and --, reported on the field. Writes through ref or out, to an element ("cache[0] = x")
        // and to static properties aren't counted, and neither are fields declared outside this
        // compilation, such as EditorApplication.delayCall, which Unity APIs are built around.
        public static List<(IFieldSymbol Field, Location Location)> FirstPerField(SemanticModel model, SyntaxNode code, CancellationToken ct)
        {
            List<(IFieldSymbol, Location)> writes = new List<(IFieldSymbol, Location)>();
            HashSet<IFieldSymbol> seen = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
            foreach (SyntaxNode node in code.DescendantNodesAndSelf())
            {
                ExpressionSyntax target;
                TextSpan span;
                switch (node)
                {
                    case AssignmentExpressionSyntax assignment:
                        target = assignment.Left;
                        span = TextSpan.FromBounds(assignment.Left.SpanStart, assignment.OperatorToken.Span.End);
                        break;
                    case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                        target = postfix.Operand;
                        span = postfix.Operand.Span;
                        break;
                    case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                        target = prefix.Operand;
                        span = prefix.Operand.Span;
                        break;
                    default:
                        continue;
                }
                if (!(model.GetSymbolInfo(target, ct).Symbol is IFieldSymbol field) || !field.IsStatic || field.IsConst || !field.Locations.Any(location => location.IsInSource)) continue;
                if (seen.Add(field)) writes.Add((field, Location.Create(node.SyntaxTree, span)));
            }
            return writes;
        }
    }
}