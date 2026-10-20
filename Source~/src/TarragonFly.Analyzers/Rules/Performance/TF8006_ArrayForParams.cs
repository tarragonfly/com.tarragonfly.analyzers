using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
    // Only arrays with an initializer count, empty ones included, in calls, "new", indexers,
    // constructor initializers and attributes; target-typed "new(...)" isn't checked. Arrays of
    // object aren't checked, since "new object[] { x }" can mean something different from passing x.
    // Nor are arrays whose items, passed in their place, would make the call bind to the member it's
    // in, as ": this(new[] { renderer })" would in that constructor. Same for arrays whose items
    // wouldn't compile there, as when they'd land on an optional parameter left out before the params
    // one: Label("x", types: new[] { a, b }) for Label(string name, string group = "", params Type[]
    // types). Items that pick another overload still count: name.Split(new[] { ' ' }) is reported,
    // since name.Split(' ') does the same. Reported on the argument.
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
                InitializerExpressionSyntax initializer = Initializer(argument.Expression);
                if (initializer == null || !(argument.Parent is BaseArgumentListSyntax list)) return;
                if (!(context.SemanticModel.GetOperation(argument, context.CancellationToken) is IArgumentOperation operation)) return;
                if (operation.ArgumentKind != ArgumentKind.Explicit || operation.Parameter == null || !operation.Parameter.IsParams) return;
                if (!IsArrayOfItems(context, argument.Expression) || !CanPassItems(list.Arguments, argument, argument.NameColon != null, initializer, operation.Parameter)) return;

                BaseArgumentListSyntax expanded = list.WithArguments(WithItems(list.Arguments, argument, initializer));
                ISymbol called;
                switch (list.Parent)
                {
                    case InvocationExpressionSyntax invocation:
                        called = Speculate(context, invocation, invocation.WithArgumentList((ArgumentListSyntax)expanded));
                        break;
                    case ObjectCreationExpressionSyntax creation:
                        called = Speculate(context, creation, creation.WithArgumentList((ArgumentListSyntax)expanded));
                        break;
                    case ElementAccessExpressionSyntax access:
                        called = Speculate(context, access, access.WithArgumentList((BracketedArgumentListSyntax)expanded));
                        break;
                    case ConstructorInitializerSyntax constructorInitializer:
                        called = context.SemanticModel.GetSpeculativeSymbolInfo(constructorInitializer.SpanStart, constructorInitializer.WithArgumentList((ArgumentListSyntax)expanded)).Symbol;
                        break;
                    default:
                        return;
                }
                if (called == null || SymbolEqualityComparer.Default.Equals(called.OriginalDefinition, context.ContainingSymbol?.OriginalDefinition)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, argument.GetLocation(), operation.Parameter.Name));
            }), SyntaxKind.Argument);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AttributeArgumentSyntax argument = (AttributeArgumentSyntax)context.Node;
                InitializerExpressionSyntax initializer = Initializer(argument.Expression);
                if (initializer == null || argument.NameEquals != null || !(argument.Parent is AttributeArgumentListSyntax list) || !(list.Parent is AttributeSyntax attribute)) return;
                if (!(context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is IMethodSymbol constructor)) return;
                IParameterSymbol parameter = ParameterOf(list.Arguments, argument, constructor);
                if (parameter == null || !parameter.IsParams || !IsArrayOfItems(context, argument.Expression)) return;
                // Only the array in its own slot, not an earlier argument that happens to be one.
                int position = list.Arguments.IndexOf(argument);
                if (argument.NameColon == null && (position != parameter.Ordinal || position != list.Arguments.Count(a => a.NameEquals == null) - 1)) return;
                if (!CanPassItems(list.Arguments, argument, argument.NameColon != null, initializer, parameter)) return;

                AttributeSyntax expanded = attribute.WithArgumentList(list.WithArguments(WithItems(list.Arguments, argument, initializer)));
                if (context.SemanticModel.GetSpeculativeSymbolInfo(attribute.SpanStart, expanded).Symbol == null) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, argument.GetLocation(), parameter.Name));
            }), SyntaxKind.AttributeArgument);
        }

        private static InitializerExpressionSyntax Initializer(ExpressionSyntax expression)
        {
            switch (expression)
            {
                case ArrayCreationExpressionSyntax creation: return creation.Initializer;
                case ImplicitArrayCreationExpressionSyntax implicitCreation: return implicitCreation.Initializer;
                default: return null;
            }
        }

        private static bool IsArrayOfItems(SyntaxNodeAnalysisContext context, ExpressionSyntax array)
        {
            return context.SemanticModel.GetTypeInfo(array, context.CancellationToken).Type is IArrayTypeSymbol type && type.ElementType.SpecialType != SpecialType.System_Object;
        }

        // A named params argument takes one item at most; more need the name dropped, which only works
        // when every parameter before it already has a positional argument.
        private static bool CanPassItems<TArgument>(SeparatedSyntaxList<TArgument> arguments, TArgument argument, bool named, InitializerExpressionSyntax initializer, IParameterSymbol parameter)
            where TArgument : SyntaxNode
        {
            return !named || initializer.Expressions.Count < 2 || arguments.IndexOf(argument) == parameter.Ordinal;
        }

        // The argument list with the array's items in place of the array, keeping its name for one item.
        private static SeparatedSyntaxList<ArgumentSyntax> WithItems(SeparatedSyntaxList<ArgumentSyntax> arguments, ArgumentSyntax array, InitializerExpressionSyntax initializer)
        {
            List<ArgumentSyntax> items = initializer.Expressions.Select(item => SyntaxFactory.Argument(item)).ToList();
            if (items.Count == 0) return arguments.Remove(array);
            if (items.Count == 1 && array.NameColon != null) items[0] = items[0].WithNameColon(array.NameColon);
            return arguments.ReplaceRange(array, items);
        }

        private static SeparatedSyntaxList<AttributeArgumentSyntax> WithItems(SeparatedSyntaxList<AttributeArgumentSyntax> arguments, AttributeArgumentSyntax array, InitializerExpressionSyntax initializer)
        {
            List<AttributeArgumentSyntax> items = initializer.Expressions.Select(item => SyntaxFactory.AttributeArgument(item)).ToList();
            if (items.Count == 0) return arguments.Remove(array);
            if (items.Count == 1 && array.NameColon != null) items[0] = items[0].WithNameColon(array.NameColon);
            return arguments.ReplaceRange(array, items);
        }

        private static ISymbol Speculate(SyntaxNodeAnalysisContext context, ExpressionSyntax original, ExpressionSyntax expanded)
        {
            return context.SemanticModel.GetSpeculativeSymbolInfo(original.SpanStart, expanded, SpeculativeBindingOption.BindAsExpression).Symbol;
        }

        private static IParameterSymbol ParameterOf(SeparatedSyntaxList<AttributeArgumentSyntax> arguments, AttributeArgumentSyntax argument, IMethodSymbol constructor)
        {
            if (argument.NameColon != null) return constructor.Parameters.FirstOrDefault(parameter => parameter.Name == argument.NameColon.Name.Identifier.ValueText);
            int position = arguments.IndexOf(argument);
            return position < constructor.Parameters.Length ? constructor.Parameters[position] : null;
        }
    }
}