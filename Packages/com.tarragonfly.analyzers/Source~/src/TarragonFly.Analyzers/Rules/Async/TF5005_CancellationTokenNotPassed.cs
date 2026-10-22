using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
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
    // it, or when an overload returns the same type and takes the same parameters plus a
    // CancellationToken, and maybe optional ones after it, as UniTask.NextFrame(CancellationToken
    // cancellationToken, bool cancelImmediately = false) does for NextFrame(). UniTask.Yield() doesn't
    // count, since it returns a YieldAwaitable and Yield(ct) a UniTask. Neither does an overload that
    // is the method making the call, as when it forwards to the overload without a token. A token is
    // at hand when an enclosing method, lambda or local function has a CancellationToken parameter,
    // or the type itself declares a CancellationToken or CancellationTokenSource field or property;
    // locals and inherited members don't count. CancellationToken.None is a deliberate choice and
    // fine. TrySetCanceled, SetCanceled and FromCanceled aren't checked: their token only labels the
    // exception they make. A call on this object to one of the type's own methods is fine when that
    // method, or a method of the type it calls on this object in turn, already reads the field or
    // property the token would come from. The usual case: ShowAsync goes through a RunTransitionAsync
    // that links _transitionCancellation's token itself. Only methods declared in the same file are
    // followed. Reported on the called method.
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
                string token = TokenAtHand(context, invocation, operation.TargetMethod, invocation.Parent is AwaitExpressionSyntax);
                if (token != null) context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), operation.TargetMethod.Name, token));
            }), SyntaxKind.InvocationExpression);
        }

        // Also used by TF5103, which stays quiet where this rule already reports.
        internal static bool MissesToken(SemanticModel model, InvocationExpressionSyntax invocation, IInvocationOperation operation)
        {
            if (TokenOnlyLabelsTheException(operation.TargetMethod.Name)) return false;
            bool takesToken = false;
            foreach (IArgumentOperation argument in operation.Arguments)
            {
                if (!UniTask.IsCancellationToken(argument.Parameter.Type)) continue;
                takesToken = true;
                if (argument.ArgumentKind == ArgumentKind.DefaultValue || IsDefault(argument.Value.Syntax)) return true;
            }
            return !takesToken && HasTokenOverload(model, invocation.SpanStart, operation.TargetMethod, CallingMember(model, invocation.SpanStart));
        }

        private static bool TokenOnlyLabelsTheException(string name)
        {
            return name == "TrySetCanceled" || name == "SetCanceled" || name == "FromCanceled";
        }

        // The method, constructor or accessor the call is in, outside any lambda or local function.
        private static ISymbol CallingMember(SemanticModel model, int position)
        {
            ISymbol member = model.GetEnclosingSymbol(position);
            while (member is IMethodSymbol method && (method.MethodKind == MethodKind.AnonymousFunction || method.MethodKind == MethodKind.LocalFunction)) member = member.ContainingSymbol;
            return member;
        }

        private static bool IsDefault(SyntaxNode value)
        {
            return value.IsKind(SyntaxKind.DefaultLiteralExpression) || value.IsKind(SyntaxKind.DefaultExpression);
        }

        // An accessible overload with the same return type and parameters plus one CancellationToken,
        // other than the method making the call. Anything it takes on top has to be optional and come
        // after the token.
        private static bool HasTokenOverload(SemanticModel model, int position, IMethodSymbol method, ISymbol caller)
        {
            IMethodSymbol original = (method.ReducedFrom ?? method).OriginalDefinition;
            foreach (IMethodSymbol overload in original.ContainingType.GetMembers(original.Name).OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(overload, caller?.OriginalDefinition)) continue;
                if (overload.IsStatic != original.IsStatic || overload.Arity != original.Arity || overload.Parameters.Length <= original.Parameters.Length) continue;
                if (overload.ReturnType.ToDisplayString() != original.ReturnType.ToDisplayString()) continue;
                IParameterSymbol[] tokens = overload.Parameters.Where(p => UniTask.IsCancellationToken(p.Type)).ToArray();
                if (tokens.Length != 1 || !model.IsAccessible(position, overload)) continue;
                IParameterSymbol[] others = overload.Parameters.Where(p => !UniTask.IsCancellationToken(p.Type)).ToArray();
                bool same = true;
                for (int i = 0; i < others.Length && same; i++)
                {
                    same = i < original.Parameters.Length
                        ? others[i].RefKind == original.Parameters[i].RefKind && others[i].Type.ToDisplayString() == original.Parameters[i].Type.ToDisplayString()
                        : others[i].IsOptional && others[i].Ordinal > tokens[0].Ordinal;
                }
                if (same) return true;
            }
            return false;
        }

        // How the code here would pass a token, or null when it has none.
        private static string TokenAtHand(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax node, IMethodSymbol callee, bool awaited)
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
                bool isToken = UniTask.IsCancellationToken(type);
                if (!isToken && !SymbolNames.IsNamed(type, "System.Threading.CancellationTokenSource")) continue;
                if (CalleeReadsMember(context.SemanticModel, node, callee, candidate, context.CancellationToken)) return null;
                return isToken ? candidate.Name : candidate.Name + ".Token";
            }
            return awaited && !member.IsStatic && Frameworks.Unity.IsMonoBehaviour(member.ContainingType) ? "destroyCancellationToken" : null;
        }

        // The call goes to a method of this type on this object, and that method, or a method of the
        // type it calls on this object in turn, reads tokenMember. Matched by symbol, so a parameter
        // with the member's name doesn't count. Only methods declared in this file are followed, since
        // the semantic model only covers this file; a partial type split across files still gets reported.
        private static bool CalleeReadsMember(SemanticModel model, InvocationExpressionSyntax invocation, IMethodSymbol callee, ISymbol tokenMember, CancellationToken ct)
        {
            INamedTypeSymbol type = tokenMember.ContainingType;
            if (!IsOwnInstanceCall(invocation, callee, type)) return false;
            foreach (SyntaxNode body in Declarations(model, callee))
            {
                if (ReadsMember(model, body, tokenMember, ct)) return true;
                foreach (InvocationExpressionSyntax inner in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!(model.GetSymbolInfo(inner, ct).Symbol is IMethodSymbol next) || !IsOwnInstanceCall(inner, next, type)) continue;
                    if (Declarations(model, next).Any(nextBody => ReadsMember(model, nextBody, tokenMember, ct))) return true;
                }
            }
            return false;
        }

        // "Run(...)" or "this.Run(...)" calling an instance method of the type.
        private static bool IsOwnInstanceCall(InvocationExpressionSyntax invocation, IMethodSymbol method, INamedTypeSymbol type)
        {
            bool onThis = invocation.Expression is SimpleNameSyntax || invocation.Expression is MemberAccessExpressionSyntax access && access.Expression is ThisExpressionSyntax;
            return onThis && !method.IsStatic && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, type);
        }

        private static IEnumerable<SyntaxNode> Declarations(SemanticModel model, IMethodSymbol method)
        {
            return method.OriginalDefinition.DeclaringSyntaxReferences.Where(reference => reference.SyntaxTree == model.SyntaxTree).Select(reference => reference.GetSyntax());
        }

        private static bool ReadsMember(SemanticModel model, SyntaxNode body, ISymbol member, CancellationToken ct)
        {
            return body.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => identifier.Identifier.ValueText == member.Name
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, ct).Symbol?.OriginalDefinition, member.OriginalDefinition));
        }
    }
}