using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9001: a [TestFixture] class with no tests of its own or inherited.
    //
    // The Test Runner shows it as passing while it checks nothing, often because the test attributes
    // were forgotten or the tests moved elsewhere.
    //
    // Tests are methods with [Test], [TestCase], [TestCaseSource], [Theory] or [UnityTest]. Record
    // fixtures count too. Abstract fixtures are base classes and aren't checked. Reported on the class
    // name. Test code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TestClassWithoutTestsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9001",
            "Test fixture with no tests",
            "'{0}' is a test fixture with no tests; add [Test] or [UnityTest] to its tests, or remove [TestFixture]",
            "Tests",
            CodeKind.Tests);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeDeclarationSyntax declaration = (TypeDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol type) || type.IsAbstract) return;
                if (!Frameworks.NUnit.HasTestClassAttribute(type) || Frameworks.NUnit.HasTests(type)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), type.Name));
            }), SyntaxKind.ClassDeclaration, SyntaxKind.RecordDeclaration);
        }
    }
}
