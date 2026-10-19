using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5001: a disposable class disposing one of its fields somewhere other than its own Dispose, as
    // in "cancel.Dispose();" inside Restart() of a class that implements IDisposable.
    //
    // The field then lives on in a disposed state: a later Dispose disposes it twice, and code that
    // still uses it, such as a CancellationTokenSource, throws ObjectDisposedException. Dispose fields
    // in Dispose, and replace them with a new instance where the class keeps going.
    //
    // Calls to Dispose() on a field of this object, directly, through "this." or with ?., count in any
    // method except Dispose() and Dispose(bool), and lambdas inside them. Static fields and locals
    // aren't checked, nor are classes that don't implement IDisposable. Reported on the Dispose name.
    // Game code only.
    //
    // Unity objects aren't checked, since a MonoBehaviour releases what it owns in OnDisable or
    // OnDestroy, never through IDisposable.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldDisposedOutsideDisposeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5001",
            "Field disposed outside Dispose",
            "'{0}' is disposed here but stays in the field; dispose it in Dispose, or replace it with a new instance",
            "Async",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 0) return;
                SimpleNameSyntax name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name
                    : invocation.Expression is MemberBindingExpressionSyntax binding ? binding.Name : null;
                if (name == null || name.Identifier.ValueText != "Dispose") return;

                ExpressionSyntax receiver = Receiver(invocation);
                if (receiver == null || !(context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is IFieldSymbol field) || field.IsStatic) return;
                if (receiver is MemberAccessExpressionSyntax qualified && !(qualified.Expression is ThisExpressionSyntax)) return;

                IMethodSymbol member = EnclosingMember(context);
                if (member == null || IsDisposeMethod(member) || !SymbolEqualityComparer.Default.Equals(field.ContainingType, member.ContainingType)) return;
                INamedTypeSymbol type = member.ContainingType;
                if (Frameworks.Unity.IsUnityObject(type) || !SymbolNames.Implements(type, candidate => SymbolNames.IsNamed(candidate, "System.IDisposable"))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), field.Name));
            }), SyntaxKind.InvocationExpression);
        }

        // "field" in field.Dispose(), this.field.Dispose() and field?.Dispose().
        private static ExpressionSyntax Receiver(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax access) return access.Expression;
            return invocation.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == invocation ? conditional.Expression : null;
        }

        private static IMethodSymbol EnclosingMember(SyntaxNodeAnalysisContext context)
        {
            ISymbol symbol = context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart, context.CancellationToken);
            while (symbol is IMethodSymbol method && (method.MethodKind == MethodKind.AnonymousFunction || method.MethodKind == MethodKind.LocalFunction)) symbol = symbol.ContainingSymbol;
            return symbol as IMethodSymbol;
        }

        private static bool IsDisposeMethod(IMethodSymbol method)
        {
            if (method.Name != "Dispose" && !method.Name.EndsWith(".Dispose", System.StringComparison.Ordinal)) return false;
            return method.Parameters.Length == 0 || (method.Parameters.Length == 1 && method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean);
        }
    }
}