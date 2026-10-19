using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8006: an array written out by hand for a params parameter, as in "Spawn(new[] { goblin, orc })"
    // for "void Spawn(params Enemy[] enemies)".
    //
    // The compiler builds that array anyway; passing the items, "Spawn(goblin, orc)", reads better.
    //
    // Only arrays with an initializer count, empty ones included. Arrays of object aren't checked,
    // since "new object[] { x }" can mean something different from passing x. Reported on the argument.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ArrayForParamsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8006",
            "Array built by hand for a params parameter",
            "Pass the items directly; '{0}' is a params parameter and builds the array itself",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ArgumentSyntax argument = (ArgumentSyntax)context.Node;
                InitializerExpressionSyntax initializer = argument.Expression is ArrayCreationExpressionSyntax creation ? creation.Initializer
                    : argument.Expression is ImplicitArrayCreationExpressionSyntax implicitCreation ? implicitCreation.Initializer : null;
                if (initializer == null) return;
                if (!(context.SemanticModel.GetOperation(argument, context.CancellationToken) is IArgumentOperation operation)) return;
                if (operation.ArgumentKind != ArgumentKind.Explicit || operation.Parameter == null || !operation.Parameter.IsParams) return;
                if (context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).Type is IArrayTypeSymbol array
                    && array.ElementType.SpecialType != SpecialType.System_Object)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, argument.GetLocation(), operation.Parameter.Name));
                }
            }), SyntaxKind.Argument);
        }
    }
}