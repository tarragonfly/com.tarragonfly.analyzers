using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8015: LINQ's Min() or Max() on a SortedSet, as in "scores.Max()".
    //
    // LINQ walks every item to find the extreme, while the set already keeps them sorted: its Min and
    // Max properties read the end of the tree.
    //
    // Only a fresh, nonempty SortedSet with the default comparer and built-in value elements is
    // checked. Other sets can use a different order or return default where LINQ throws on empty.
    // A local counts only when this call is its sole use. Reported on the method name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LinqMinMaxOnSortedSetAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8015",
            "LINQ Min or Max on a SortedSet",
            "Use the SortedSet's {0} property; {0}() walks every item",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (invocation.ArgumentList.Arguments.Count != 0) return;
                IMethodSymbol method = LinqCalls.Method(context.SemanticModel, invocation, context.CancellationToken);
                if (method == null || (method.Name != "Min" && method.Name != "Max")) return;
                ITypeSymbol receiver = LinqCalls.ReceiverType(context.SemanticModel, invocation, context.CancellationToken);
                if (!SymbolNames.IsNamed(receiver, "System.Collections.Generic.SortedSet`1") || !HasMatchingExtrema(context, invocation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, LinqCalls.NameLocation(invocation), method.Name));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool HasMatchingExtrema(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation)
        {
            ExpressionSyntax receiver = LinqCalls.Receiver(invocation);
            ExpressionSyntax initial = receiver;
            if (context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is ILocalSymbol local)
            {
                if (local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax variable) return false;
                SyntaxNode body = variable.FirstAncestorOrSelf<BlockSyntax>();
                if (body == null || body.DescendantNodes().OfType<IdentifierNameSyntax>().Count(name =>
                    SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, local)) != 1) return false;
                initial = variable.Initializer?.Value;
            }
            if (initial is not BaseObjectCreationExpressionSyntax creation || creation.ArgumentList?.Arguments.Count > 0
                || creation.Initializer == null || creation.Initializer.Expressions.Count == 0) return false;
            // A subclass can pass its own comparer to the base constructor.
            if (context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is not INamedTypeSymbol set
                || !SymbolNames.IsNamed(set.OriginalDefinition, "System.Collections.Generic.SortedSet`1")) return false;
            ITypeSymbol item = set.TypeArguments[0];
            if (item.TypeKind != TypeKind.Enum && item.SpecialType != SpecialType.System_String && item.SpecialType != SpecialType.System_Boolean
                && (item.SpecialType < SpecialType.System_Char || item.SpecialType > SpecialType.System_Decimal)) return false;
            foreach (ExpressionSyntax element in creation.Initializer.Expressions)
            {
                Optional<object> value = context.SemanticModel.GetConstantValue(element, context.CancellationToken);
                if (!value.HasValue || value.Value == null || value.Value is float or double) return false;
            }
            return true;
        }
    }
}
