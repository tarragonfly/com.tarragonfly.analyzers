using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8003: a property getter that copies a collection, as in "public List<Item> Items =>
    // items.ToList();".
    //
    // Reading a property looks cheap, so callers read it in loops and every frame, each time
    // allocating a full copy. Make it a method (GetItems()) so the cost shows, or return a read-only
    // view such as IReadOnlyList<T>.
    //
    // Copies: LINQ's ToList and ToArray, an array's Clone(), and "new" of a collection given another
    // collection, such as new List<T>(items). They count in what the getter returns, its expression
    // body or a return statement, anywhere in it except as an argument to another call, so
    // "items.ToList().Count" counts. A copy stored in a local first or used in a loop isn't counted,
    // and neither are List.ToArray, filtering with Where, an empty new collection, a read-only wrapper
    // such as new ReadOnlyCollection<T>(items), which copies nothing, copies cached in a field
    // ("cache ??= items.ToArray()"), indexers and lambdas inside the getter. Reported on the copy.
    // Game code only.
    //
    // Properties marked with Odin's [ShowInInspector] are skipped, since they hand the Inspector a copy
    // on purpose so it can't change the real collection.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PropertyCopiesCollectionAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8003",
            "Property copies a collection on every read",
            "'{0}' copies the collection every time it is read; make it a method or return a read-only view",
            "Performance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                PropertyDeclarationSyntax property = (PropertyDeclarationSyntax)context.Node;
                SyntaxNode getter = property.ExpressionBody;
                if (getter == null && property.AccessorList != null)
                {
                    foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
                    {
                        if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) getter = (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
                    }
                }
                if (getter == null) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is IPropertySymbol symbol)) return;
                if (SymbolNames.HasAttribute(symbol, "Sirenix.OdinInspector.ShowInInspectorAttribute")) return;

                foreach (ExpressionSyntax returned in Returned(getter))
                {
                    foreach (SyntaxNode node in returned.DescendantNodesAndSelf(child => !(child is AnonymousFunctionExpressionSyntax)))
                    {
                        Location copy = CopyLocation(context, node);
                        if (copy != null && !IsCachedInField(context, node) && !IsArgument(node)) context.ReportDiagnostic(Diagnostic.Create(Rule, copy, symbol.Name));
                    }
                }
            }), SyntaxKind.PropertyDeclaration);
        }

        // The expression body, or what the return statements give back.
        private static IEnumerable<ExpressionSyntax> Returned(SyntaxNode getter)
        {
            if (getter is ArrowExpressionClauseSyntax arrow) return new[] { arrow.Expression };
            return getter.DescendantNodes(child => !(child is AnonymousFunctionExpressionSyntax) && !(child is LocalFunctionStatementSyntax))
                .OfType<ReturnStatementSyntax>()
                .Where(statement => statement.Expression != null)
                .Select(statement => statement.Expression);
        }

        private static bool IsArgument(SyntaxNode copy)
        {
            SyntaxNode node = copy;
            while (node.Parent is ParenthesizedExpressionSyntax) node = node.Parent;
            return node.Parent is ArgumentSyntax;
        }

        // The copy is assigned to a field, as in "cache ??= items.ToArray()", so later reads reuse it.
        private static bool IsCachedInField(SyntaxNodeAnalysisContext context, SyntaxNode copy)
        {
            SyntaxNode node = copy;
            while (node.Parent is ParenthesizedExpressionSyntax || node.Parent is CastExpressionSyntax) node = node.Parent;
            return node.Parent is AssignmentExpressionSyntax assignment && assignment.Right == node
                && context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is IFieldSymbol;
        }

        private static Location CopyLocation(SyntaxNodeAnalysisContext context, SyntaxNode node)
        {
            if (node is InvocationExpressionSyntax invocation)
            {
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return null;
                bool linqCopy = (method.Name == "ToList" || method.Name == "ToArray") && method.ReducedFrom != null
                    && SymbolNames.IsNamed(method.ReducedFrom.ContainingType, "System.Linq.Enumerable");
                bool arrayClone = method.Name == "Clone" && method.ContainingType.SpecialType == SpecialType.System_Array;
                return linqCopy || arrayClone ? invocation.Expression.GetLocation() : null;
            }
            if (node is BaseObjectCreationExpressionSyntax creation && creation.ArgumentList != null)
            {
                ITypeSymbol created = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
                if (!IsCollection(created) || IsReadOnlyWrapper(created)) return null;
                foreach (ArgumentSyntax argument in creation.ArgumentList.Arguments)
                {
                    if (IsCollection(context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).Type)) return creation.GetLocation();
                }
            }
            return null;
        }

        // ReadOnlyCollection<T>, ReadOnlyDictionary<TKey, TValue> and ReadOnlyObservableCollection<T>
        // wrap the collection they're given.
        private static bool IsReadOnlyWrapper(ITypeSymbol type)
        {
            return SymbolNames.IsInNamespace(type, "System.Collections.ObjectModel") && type.Name.StartsWith("ReadOnly", System.StringComparison.Ordinal);
        }

        private static bool IsCollection(ITypeSymbol type)
        {
            if (type == null || type.SpecialType == SpecialType.System_String) return false;
            return type is IArrayTypeSymbol || SymbolNames.Implements(type, candidate => candidate.SpecialType == SpecialType.System_Collections_IEnumerable);
        }
    }
}