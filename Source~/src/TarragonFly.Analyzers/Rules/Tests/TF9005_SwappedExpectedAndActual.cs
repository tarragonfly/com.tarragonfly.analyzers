using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9005: the value under test passed as the expected value and a constant as the actual one, as in
    // "Assert.AreEqual(player.Health, 100)".
    //
    // The test still passes and fails at the right times, but a failure reads backwards ("Expected:
    // 90 But was: 100"), which sends whoever reads it after the wrong number.
    //
    // Checked: Assert.AreEqual, AreNotEqual, AreSame and AreNotSame where actual is a constant (a
    // literal or a const) and expected isn't, and Assert.That(constant, Is.EqualTo(value)), with
    // Is.Not.EqualTo and Is.SameAs. Reported from the first argument to the second.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SwappedExpectedAndActualAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9005",
            "Expected and actual swapped in an assertion",
            "Put the constant first as the expected value and the value under test second; swapped, the failure message reads backwards",
            "Tests",
            CodeKind.Tests);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count < 2 || !(invocation.Expression is MemberAccessExpressionSyntax access)) return;
                string name = access.Name.Identifier.ValueText;
                if (name != "AreEqual" && name != "AreNotEqual" && name != "AreSame" && name != "AreNotSame" && name != "That") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || !BoolLiteralInAssertionAnalyzer.IsAssert(method.ContainingType)) return;

                ArgumentSyntax first = invocation.ArgumentList.Arguments[0];
                ArgumentSyntax second = invocation.ArgumentList.Arguments[1];
                if (first.NameColon != null || second.NameColon != null) return;
                bool swapped = name == "That"
                    ? IsConstant(context, first.Expression) && ExpectedOfConstraint(context, second.Expression) is ExpressionSyntax expected && !IsConstant(context, expected)
                    : method.Parameters.Length >= 2 && method.Parameters[0].Name == "expected" && method.Parameters[1].Name == "actual"
                        && IsConstant(context, second.Expression) && !IsConstant(context, first.Expression);
                if (!swapped) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(invocation.SyntaxTree, TextSpan.FromBounds(first.SpanStart, second.Span.End))));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool IsConstant(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
        {
            return context.SemanticModel.GetConstantValue(expression, context.CancellationToken).HasValue;
        }

        // The value inside Is.EqualTo(x), Is.Not.EqualTo(x) or Is.SameAs(x).
        private static ExpressionSyntax ExpectedOfConstraint(SyntaxNodeAnalysisContext context, ExpressionSyntax constraint)
        {
            if (!(constraint is InvocationExpressionSyntax call) || call.ArgumentList.Arguments.Count != 1 || !(call.Expression is MemberAccessExpressionSyntax access)) return null;
            string name = access.Name.Identifier.ValueText;
            if (name != "EqualTo" && name != "SameAs") return null;
            if (!(context.SemanticModel.GetSymbolInfo(call, context.CancellationToken).Symbol is IMethodSymbol method)) return null;
            bool nunit = SymbolNames.IsNamed(method.ContainingType, "NUnit.Framework.Is") || SymbolNames.IsNamed(method.ContainingType, "NUnit.Framework.Constraints.ConstraintExpression");
            return nunit ? call.ArgumentList.Arguments[0].Expression : null;
        }
    }
}