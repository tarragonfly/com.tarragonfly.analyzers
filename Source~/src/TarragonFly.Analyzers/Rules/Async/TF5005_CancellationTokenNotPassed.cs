using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5005: a call that could take a CancellationToken made without one, while a token is at hand, as
    // in "await UniTask.Delay(500);" in "async UniTask FadeIn(CancellationToken ct)".
    //
    // The call can't be cancelled, so it keeps running after the scene unloads or the object is
    // destroyed, and its continuation then touches destroyed objects (MissingReferenceException) or
    // runs a fade twice.
    //
    // A call counts when it leaves out an optional CancellationToken parameter or passes default for
    // it, or when an overload takes the same parameters plus a CancellationToken. A token is at hand
    // when an enclosing method, lambda or local function has a CancellationToken parameter, or the
    // type itself declares a CancellationToken or CancellationTokenSource field or property; locals and
    // inherited members don't count. CancellationToken.None is a deliberate choice and fine. Reported
    // on the called method.
    //
    // In instance members of a MonoBehaviour with no other token, Unity's destroyCancellationToken
    // counts as the token at hand for awaited calls, so the wait ends with the object. Calls that
    // aren't awaited, such as TrySetCanceled() in OnDestroy, aren't checked against it.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CancellationTokenNotPassedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5005",
            "CancellationToken not passed on",
            "'{0}' can take a CancellationToken; pass {1} so it stops when the work is cancelled",
            "Async",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation)) return;
                if (!MissesToken(context.SemanticModel, invocation, operation)) return;
                string token = TokenAtHand(context, invocation, invocation.Parent is AwaitExpressionSyntax);
                if (token != null) context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), operation.TargetMethod.Name, token));
            }), SyntaxKind.InvocationExpression);
        }

        // Also used by TF5103, which stays quiet where this rule already reports.
        internal static bool MissesToken(SemanticModel model, InvocationExpressionSyntax invocation, IInvocationOperation operation)
        {
            bool takesToken = false;
            foreach (IArgumentOperation argument in operation.Arguments)
            {
                if (!UniTask.IsCancellationToken(argument.Parameter.Type)) continue;
                takesToken = true;
                if (argument.ArgumentKind == ArgumentKind.DefaultValue || IsDefault(argument.Value.Syntax)) return true;
            }
            return !takesToken && HasTokenOverload(model, invocation.SpanStart, operation.TargetMethod);
        }

        private static bool IsDefault(SyntaxNode value)
        {
            return value.IsKind(SyntaxKind.DefaultLiteralExpression) || value.IsKind(SyntaxKind.DefaultExpression);
        }

        // An accessible overload with the same parameters plus one CancellationToken.
        private static bool HasTokenOverload(SemanticModel model, int position, IMethodSymbol method)
        {
            IMethodSymbol original = (method.ReducedFrom ?? method).OriginalDefinition;
            foreach (IMethodSymbol overload in original.ContainingType.GetMembers(original.Name).OfType<IMethodSymbol>())
            {
                if (overload.IsStatic != original.IsStatic || overload.Arity != original.Arity || overload.Parameters.Length != original.Parameters.Length + 1) continue;
                if (overload.Parameters.Count(p => UniTask.IsCancellationToken(p.Type)) != 1 || !model.IsAccessible(position, overload)) continue;
                IParameterSymbol[] others = overload.Parameters.Where(p => !UniTask.IsCancellationToken(p.Type)).ToArray();
                bool same = true;
                for (int i = 0; i < others.Length && same; i++)
                {
                    same = others[i].RefKind == original.Parameters[i].RefKind && others[i].Type.ToDisplayString() == original.Parameters[i].Type.ToDisplayString();
                }
                if (same) return true;
            }
            return false;
        }

        // How the code here would pass a token, or null when it has none.
        private static string TokenAtHand(SyntaxNodeAnalysisContext context, SyntaxNode node, bool awaited)
        {
            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                IMethodSymbol owner = ancestor is AnonymousFunctionExpressionSyntax lambda ? context.SemanticModel.GetSymbolInfo(lambda, context.CancellationToken).Symbol as IMethodSymbol
                    : ancestor is LocalFunctionStatementSyntax || ancestor is BaseMethodDeclarationSyntax || ancestor is AccessorDeclarationSyntax
                        ? context.SemanticModel.GetDeclaredSymbol(ancestor, context.CancellationToken) as IMethodSymbol : null;
                IParameterSymbol parameter = owner?.Parameters.FirstOrDefault(p => UniTask.IsCancellationToken(p.Type));
                if (parameter != null) return parameter.Name;
                if (ancestor is BaseTypeDeclarationSyntax) break;
            }

            ISymbol member = context.SemanticModel.GetEnclosingSymbol(node.SpanStart, context.CancellationToken);
            while (member is IMethodSymbol method && (method.MethodKind == MethodKind.AnonymousFunction || method.MethodKind == MethodKind.LocalFunction)) member = member.ContainingSymbol;
            if (member?.ContainingType == null) return null;
            foreach (ISymbol candidate in member.ContainingType.GetMembers())
            {
                if (candidate.IsStatic != member.IsStatic && !candidate.IsStatic) continue;
                ITypeSymbol type = candidate is IFieldSymbol field && !field.IsImplicitlyDeclared ? field.Type : (candidate as IPropertySymbol)?.Type;
                if (UniTask.IsCancellationToken(type)) return candidate.Name;
                if (SymbolNames.IsNamed(type, "System.Threading.CancellationTokenSource")) return candidate.Name + ".Token";
            }
            return awaited && !member.IsStatic && Frameworks.Unity.IsMonoBehaviour(member.ContainingType) ? "destroyCancellationToken" : null;
        }
    }
}