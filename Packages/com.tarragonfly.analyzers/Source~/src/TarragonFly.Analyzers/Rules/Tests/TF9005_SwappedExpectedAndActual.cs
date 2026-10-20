using System;
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
    // literal or a const) and expected isn't, also when the arguments are named, as in
    // "Assert.AreEqual(expected: lives, actual: 3)". And Assert.That(constant, Is.EqualTo(value)),
    // with Is.Not.EqualTo and Is.SameAs, written without argument names. Reported from the first of
    // the two arguments to the other.
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

                SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
                ArgumentSyntax expected;
                ArgumentSyntax actual;
                if (name == "That")
                {
                    if (arguments[0].NameColon != null || arguments[1].NameColon != null) return;
                    actual = arguments[0];
                    expected = arguments[1];
                    if (!IsConstant(context, actual.Expression) || !(ExpectedOfConstraint(context, expected.Expression) is ExpressionSyntax value) || IsConstant(context, value)) return;
                }
                else
                {
                    expected = ArgumentFor(method, arguments, "expected");
                    actual = ArgumentFor(method, arguments, "actual");
                    if (expected == null || actual == null || !IsConstant(context, actual.Expression) || IsConstant(context, expected.Expression)) return;
                }
                TextSpan span = TextSpan.FromBounds(Math.Min(expected.SpanStart, actual.SpanStart), Math.Max(expected.Span.End, actual.Span.End));
                context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(invocation.SyntaxTree, span)));
            }), SyntaxKind.InvocationExpression);
        }

        // A positional argument always sits at its parameter's position: C# allows a named one
        // before it only when that one is in its own position too.
        private static ArgumentSyntax ArgumentFor(IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments, string parameterName)
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                string name = arguments[i].NameColon?.Name.Identifier.ValueText ?? (i < method.Parameters.Length ? method.Parameters[i].Name : null);
                if (name == parameterName) return arguments[i];
            }
            return null;
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