using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8018: Any(x => x == value) on a collection that has Contains, as in "unlockedLevels.Any(id =>
    // id == levelId)".
    //
    // Contains does the same check without the lambda and the LINQ enumerator.
    //
    // Checked: "x == value", "value == x" and "x.Equals(value)", where value doesn't use x, on a type
    // with known default equality: List<T> and LinkedList<T> of integral, enum, bool or string values.
    // Custom collections, sets, floating-point values and unstable search expressions are skipped.
    // Reported on the Any name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AnyInsteadOfContainsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8018",
            "Any used for an equality check",
            "Use Contains instead of Any; it checks for the value without a lambda or a LINQ enumerator",
            "Performance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 1 || LinqCalls.Method(context.SemanticModel, invocation, "Any", context.CancellationToken) == null) return;
                if (!IsEqualityCheck(context.SemanticModel, invocation, LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation)));
            }), SyntaxKind.InvocationExpression);
        }

        // Any(x => x == value) on a receiver that has Contains; the call is already known to be LINQ's Any.
        internal static bool IsEqualityCheck(SemanticModel model, InvocationExpressionSyntax any, ITypeSymbol receiver)
        {
            if (!(any.ArgumentList.Arguments[0].Expression is SimpleLambdaExpressionSyntax lambda) || !(lambda.Body is ExpressionSyntax body)) return false;
            if (!(receiver is INamedTypeSymbol named) || named.TypeArguments.Length != 1
                || (!SymbolNames.IsNamed(named, "System.Collections.Generic.List`1") && !SymbolNames.IsNamed(named, "System.Collections.Generic.LinkedList`1"))) return false;
            ITypeSymbol item = named.TypeArguments[0];
            if (item.TypeKind != TypeKind.Enum && item.SpecialType != SpecialType.System_String && item.SpecialType != SpecialType.System_Boolean
                && (item.SpecialType < SpecialType.System_Char || item.SpecialType > SpecialType.System_UInt64)) return false;
            if (!ComparesWithValue(body, lambda.Parameter.Identifier.ValueText)) return false;
            ExpressionSyntax value;
            if (body is BinaryExpressionSyntax binary)
            {
                if (model.GetOperation(binary) is not IBinaryOperation operation || operation.OperatorMethod != null && item.SpecialType != SpecialType.System_String) return false;
                value = IsParameter(binary.Left, lambda.Parameter.Identifier.ValueText) ? binary.Right : binary.Left;
            }
            else
            {
                if (item.IsReferenceType) return false;
                InvocationExpressionSyntax call = (InvocationExpressionSyntax)body;
                if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol method || !SymbolEqualityComparer.Default.Equals(method.ContainingType, item)) return false;
                value = call.ArgumentList.Arguments[0].Expression;
            }
            if (!SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(value).Type, item) && !value.IsKind(SyntaxKind.NullLiteralExpression)) return false;
            return model.GetConstantValue(value).HasValue || model.GetSymbolInfo(value).Symbol is ILocalSymbol or IParameterSymbol;
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

    }
}
