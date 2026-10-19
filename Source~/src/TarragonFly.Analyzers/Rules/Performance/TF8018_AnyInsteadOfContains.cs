using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8018: Any(x => x == value) on a collection that has Contains, as in "unlockedLevels.Any(id =>
    // id == levelId)".
    //
    // Contains does the same check without the lambda and the LINQ enumerator, and on a HashSet it is
    // a single lookup instead of a scan.
    //
    // Checked: "x == value", "value == x" and "x.Equals(value)", where value doesn't use x, on a type
    // with a public Contains method, such as List<T>, HashSet<T> and ICollection<T>. Arrays and plain
    // sequences aren't checked. Reported on the Any name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AnyInsteadOfContainsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8018",
            "Any used for an equality check",
            "Use Contains instead of Any; it checks for the value without a lambda or a LINQ enumerator",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 1 || LinqCalls.Method(context.SemanticModel, invocation, "Any", context.CancellationToken) == null) return;
                if (!IsEqualityCheck(invocation, LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation)));
            }), SyntaxKind.InvocationExpression);
        }

        // Any(x => x == value) on a receiver that has Contains; the call is already known to be LINQ's Any.
        internal static bool IsEqualityCheck(InvocationExpressionSyntax any, ITypeSymbol receiver)
        {
            if (!(any.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax lambda) || !(lambda.Body is ExpressionSyntax body)) return false;
            return ComparesWithValue(body, lambda.Parameter.Identifier.ValueText) && HasContains(receiver);
        }

        private static bool ComparesWithValue(ExpressionSyntax body, string parameter)
        {
            if (body is BinaryExpressionSyntax equals && equals.IsKind(SyntaxKind.EqualsExpression))
            {
                return (IsParameter(equals.Left, parameter) && !Uses(equals.Right, parameter))
                    || (IsParameter(equals.Right, parameter) && !Uses(equals.Left, parameter));
            }
            return body is InvocationExpressionSyntax call && call.ArgumentList.Arguments.Count == 1
                && call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Equals"
                && IsParameter(access.Expression, parameter) && !Uses(call.ArgumentList.Arguments[0].Expression, parameter);
        }

        private static bool IsParameter(ExpressionSyntax expression, string parameter)
        {
            return expression is IdentifierNameSyntax name && name.Identifier.ValueText == parameter;
        }

        private static bool Uses(ExpressionSyntax expression, string parameter)
        {
            return expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(name => name.Identifier.ValueText == parameter);
        }

        private static bool HasContains(ITypeSymbol type)
        {
            if (type == null || type is IArrayTypeSymbol) return false;
            foreach (ITypeSymbol candidate in new[] { type }.Concat(type.AllInterfaces))
            {
                for (ITypeSymbol current = candidate; current != null; current = current.BaseType)
                {
                    if (current.GetMembers("Contains").OfType<IMethodSymbol>().Any(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 1)) return true;
                }
            }
            return false;
        }
    }
}