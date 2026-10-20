using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5001: a disposable class disposing one of its fields somewhere other than its own Dispose,
    // while Dispose itself doesn't, as in "cancel.Dispose();" inside Restart() of a class whose Dispose
    // leaves cancel alone.
    //
    // Whether the field gets released then depends on Restart running, and a new instance put in the
    // field afterwards is never released by Dispose. Dispose fields in Dispose; disposing one early
    // as well, say to replace it, is fine.
    //
    // Calls to Dispose() on a field of this object, directly, through "this." or with ?., count in any
    // method except Dispose() and Dispose(bool), and lambdas inside them, unless Dispose() or
    // Dispose(bool) itself calls Dispose() on the same field. Only private and protected fields count,
    // since other code in the assembly may own and release internal or public ones, as in a cleanup
    // helper that sets the field back to null. A Dispose() method of a field type that doesn't
    // implement IDisposable counts too. Static fields and locals aren't checked, nor are classes that
    // don't implement IDisposable themselves. Reported on the Dispose name. Game code only.
    //
    // Unity objects aren't checked, since a MonoBehaviour releases what it owns in OnDisable or
    // OnDestroy, never through IDisposable.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldDisposedOutsideDisposeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5001",
            "Field disposed outside Dispose",
            "'{0}' is disposed here but not in Dispose; dispose it in Dispose too, so it's released with this object",
            "Async",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                SimpleNameSyntax name = DisposeName(invocation);
                if (name == null) return;

                ExpressionSyntax receiver = Receiver(invocation);
                if (receiver == null || !(context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is IFieldSymbol field) || field.IsStatic) return;
                if (field.DeclaredAccessibility != Accessibility.Private && field.DeclaredAccessibility != Accessibility.Protected) return;
                if (receiver is MemberAccessExpressionSyntax qualified && !(qualified.Expression is ThisExpressionSyntax)) return;

                IMethodSymbol member = EnclosingMember(context);
                if (member == null || IsDisposeMethod(member) || !SymbolEqualityComparer.Default.Equals(field.ContainingType, member.ContainingType)) return;
                INamedTypeSymbol type = member.ContainingType;
                if (Frameworks.Unity.IsUnityObject(type) || !SymbolNames.Implements(type, candidate => SymbolNames.IsNamed(candidate, "System.IDisposable"))) return;
                if (DisposeAlsoDisposes(type, field.Name, context.CancellationToken)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), field.Name));
            }), SyntaxKind.InvocationExpression);
        }

        // "Dispose" in x.Dispose() and x?.Dispose(), or null for any other call.
        private static SimpleNameSyntax DisposeName(InvocationExpressionSyntax invocation)
        {
            if (invocation.ArgumentList.Arguments.Count != 0) return null;
            SimpleNameSyntax name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name
                : invocation.Expression is MemberBindingExpressionSyntax binding ? binding.Name : null;
            return name != null && name.Identifier.ValueText == "Dispose" ? name : null;
        }

        // "field" in field.Dispose(), this.field.Dispose() and field?.Dispose().
        private static ExpressionSyntax Receiver(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax access) return access.Expression;
            return invocation.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == invocation ? conditional.Expression : null;
        }

        // Whether the type's Dispose() or Dispose(bool) calls Dispose() on the field, bare or through
        // "this.". Matched by name, since Dispose may sit in another part of a partial class.
        private static bool DisposeAlsoDisposes(INamedTypeSymbol type, string fieldName, CancellationToken cancellationToken)
        {
            foreach (ISymbol member in type.GetMembers())
            {
                if (!(member is IMethodSymbol method) || !IsDisposeMethod(method)) continue;
                foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
                {
                    foreach (InvocationExpressionSyntax call in reference.GetSyntax(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (DisposeName(call) == null) continue;
                        ExpressionSyntax receiver = Receiver(call);
                        if (receiver is MemberAccessExpressionSyntax qualified && qualified.Expression is ThisExpressionSyntax) receiver = qualified.Name;
                        if (receiver is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == fieldName) return true;
                    }
                }
            }
            return false;
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