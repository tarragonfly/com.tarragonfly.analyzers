using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    // TF3053: an iterator method (one with "yield") that checks its arguments by throwing
    // ArgumentException or one of its subclasses.
    //
    // An iterator's body only starts when the result is first enumerated, so the bad call itself
    // succeeds and the exception shows up later, somewhere else. For a coroutine that means the
    // first MoveNext, which may be far from the StartCoroutine call that passed the bad value. Check
    // in a plain wrapper method and return the iterator from a private method.
    //
    // Calls to ArgumentNullException.ThrowIfNull and the other ThrowIf helpers count. Throws inside
    // lambdas and local functions don't. Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LateIteratorArgumentCheckAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3053",
            "Argument check in an iterator runs late",
            "This argument check only runs when '{0}' is first enumerated; move it into a non-iterator wrapper method",
            "Exceptions",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
                if (method.Body == null) return;

                bool isIterator = false;
                bool checksArguments = false;
                foreach (SyntaxNode node in method.Body.DescendantNodes(n => !(n is AnonymousFunctionExpressionSyntax) && !(n is LocalFunctionStatementSyntax)))
                {
                    if (node is YieldStatementSyntax) isIterator = true;
                    else if (!checksArguments) checksArguments = IsArgumentCheck(context, node);
                }
                if (isIterator && checksArguments) context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation(), method.Identifier.ValueText));
            }), SyntaxKind.MethodDeclaration);
        }

        private static bool IsArgumentCheck(SyntaxNodeAnalysisContext context, SyntaxNode node)
        {
            switch (node)
            {
                case ThrowStatementSyntax throwStatement when throwStatement.Expression != null:
                    return IsArgumentException(context.SemanticModel.GetTypeInfo(throwStatement.Expression, context.CancellationToken).Type);
                case ThrowExpressionSyntax throwExpression:
                    return IsArgumentException(context.SemanticModel.GetTypeInfo(throwExpression.Expression, context.CancellationToken).Type);
                case InvocationExpressionSyntax invocation:
                    return context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method
                        && method.IsStatic
                        && method.Name.StartsWith("ThrowIf", StringComparison.Ordinal)
                        && IsArgumentException(method.ContainingType);
                default:
                    return false;
            }
        }

        private static bool IsArgumentException(ITypeSymbol type)
        {
            return SymbolNames.IsOrDerivesFrom(type, "System.ArgumentException");
        }
    }
}