using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9000: an NUnit [Ignore] with no Until date.
    //
    // An ignored test stops protecting anything, and without a date nothing brings it back. Fix it,
    // delete it, or give it Until = "yyyy-MM-dd" so NUnit starts running it again then.
    //
    // Checked on test methods and test classes; an [Ignore] anywhere
    // else changes nothing. Reported on the attribute. Test code only.
    //
    // It only reports an [Ignore] without a reason, which NUnit 3 requires anyway, so it never fires on
    // NUnit code. The missing Until date is the follow-up checked here.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IgnoredTestAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9000",
            "Ignored test without an end date",
            "This test is ignored with no end date; fix or delete it, or add Until = \"yyyy-MM-dd\"",
            "Tests",
            CodeKind.Tests);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AttributeSyntax attribute = (AttributeSyntax)context.Node;
                if (!SymbolNames.IsNamed(context.SemanticModel.GetTypeInfo(attribute, context.CancellationToken).Type, "NUnit.Framework.IgnoreAttribute")) return;
                if (!IgnoresTests(context, attribute)) return;
                if (attribute.ArgumentList != null)
                {
                    foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
                    {
                        if (argument.NameEquals?.Name.Identifier.ValueText == "Until"
                            && context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken).Value is string until
                            && DateTimeOffset.TryParse(until, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _)) return;
                    }
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, attribute.GetLocation()));
            }), SyntaxKind.Attribute);
        }

        private static bool IgnoresTests(SyntaxNodeAnalysisContext context, AttributeSyntax attribute)
        {
            switch (attribute.Parent?.Parent)
            {
                case MethodDeclarationSyntax method:
                    return context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is IMethodSymbol test && Frameworks.NUnit.IsTest(test);
                case TypeDeclarationSyntax type:
                    return context.SemanticModel.GetDeclaredSymbol(type, context.CancellationToken) is INamedTypeSymbol testClass
                        && (Frameworks.NUnit.HasTestClassAttribute(testClass) || Frameworks.NUnit.HasTests(testClass));
                default:
                    return false;
            }
        }
    }
}
