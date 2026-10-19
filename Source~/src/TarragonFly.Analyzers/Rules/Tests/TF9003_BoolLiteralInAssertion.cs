using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9003: true or false passed straight to an NUnit Assert method, as in "Assert.AreEqual(true,
    // door.IsOpen)" or "Assert.IsTrue(true)".
    //
    // Assert.IsTrue(true) checks nothing and Assert.That(false, ...) always fails, and
    // AreEqual(true, x) reads worse than Assert.IsTrue(x) or Assert.That(x, Is.True) and gives a
    // vaguer failure message.
    //
    // Only literals passed directly to a method of Assert count; a literal inside a constraint, as in
    // Is.EqualTo(true), or inside a larger expression, as in IsTrue(x == true), isn't checked. Reported
    // on the whole call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BoolLiteralInAssertionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9003",
            "Literal true or false in an assertion",
            "Don't pass {0} to Assert.{1}; assert on the value itself, as in Assert.That(value, Is.True)",
            "Tests",
            CodeKind.Tests);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
                {
                    if (!argument.Expression.IsKind(SyntaxKind.TrueLiteralExpression) && !argument.Expression.IsKind(SyntaxKind.FalseLiteralExpression)) continue;
                    if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || !IsAssert(method.ContainingType)) return;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), argument.Expression.ToString(), method.Name));
                    return;
                }
            }), SyntaxKind.InvocationExpression);
        }

        // NUnit's Assert, and ClassicAssert, where NUnit 4 moved the AreEqual family.
        internal static bool IsAssert(INamedTypeSymbol type)
        {
            return SymbolNames.IsNamed(type, "NUnit.Framework.Assert") || SymbolNames.IsNamed(type, "NUnit.Framework.Legacy.ClassicAssert");
        }
    }
}