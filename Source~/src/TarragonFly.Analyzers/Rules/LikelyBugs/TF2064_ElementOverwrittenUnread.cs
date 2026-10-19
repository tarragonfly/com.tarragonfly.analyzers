using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2064: the same element of a collection set twice in a row without being read in between, as in
    // "stats["hp"] = 10; stats["mp"] = 5; stats["hp"] = 20;".
    //
    // The first value is lost, which usually means a copied line kept its old key or index.
    //
    // A run is a series of statements in one block that each set an element of the same collection,
    // with "collection[key] = value" or a dictionary's Add(key, value); any other statement, or a value
    // that reads the collection, ends the run. Keys match when written the same way. Reported on the
    // statement that overwrites.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ElementOverwrittenUnreadAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2064",
            "Collection element overwritten without being read",
            "'{0}' is set again before anything reads the value set above; was another key or index meant?",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BlockSyntax block = (BlockSyntax)context.Node;
                ISymbol collection = null;
                List<SyntaxNode> keys = new List<SyntaxNode>();
                foreach (StatementSyntax statement in block.Statements)
                {
                    if (!IsElementWrite(context, statement, out ISymbol target, out SyntaxNode key, out ExpressionSyntax value, out string text)
                        || ReadsCollection(context, value, target))
                    {
                        collection = null;
                        keys.Clear();
                        continue;
                    }
                    if (!SymbolEqualityComparer.Default.Equals(collection, target))
                    {
                        collection = target;
                        keys.Clear();
                    }
                    if (keys.Any(k => SyntaxFactory.AreEquivalent(k, key))) context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetLocation(), text));
                    keys.Add(key);
                }
            }), SyntaxKind.Block);
        }

        // "collection[key] = value;" or "dictionary.Add(key, value);".
        private static bool IsElementWrite(SyntaxNodeAnalysisContext context, StatementSyntax statement, out ISymbol target, out SyntaxNode key, out ExpressionSyntax value, out string text)
        {
            target = null;
            key = null;
            value = null;
            text = null;
            if (!(statement is ExpressionStatementSyntax expression)) return false;
            if (expression.Expression is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                && assignment.Left is ElementAccessExpressionSyntax element)
            {
                target = context.SemanticModel.GetSymbolInfo(element.Expression, context.CancellationToken).Symbol;
                key = element.ArgumentList;
                value = assignment.Right;
                text = element.ToString();
                return target != null;
            }
            if (expression.Expression is InvocationExpressionSyntax invocation && invocation.ArgumentList.Arguments.Count == 2
                && invocation.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == "Add")
            {
                ITypeSymbol type = context.SemanticModel.GetTypeInfo(access.Expression, context.CancellationToken).Type;
                bool dictionary = type != null && SymbolNames.Implements(type, candidate =>
                    SymbolNames.IsNamed(candidate.OriginalDefinition, "System.Collections.Generic.IDictionary`2") || SymbolNames.IsNamed(candidate, "System.Collections.IDictionary"));
                if (!dictionary) return false;
                target = context.SemanticModel.GetSymbolInfo(access.Expression, context.CancellationToken).Symbol;
                key = SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(invocation.ArgumentList.Arguments[0]));
                value = invocation.ArgumentList.Arguments[1].Expression;
                text = access.Expression + "[" + invocation.ArgumentList.Arguments[0] + "]";
                return target != null;
            }
            return false;
        }

        private static bool ReadsCollection(SyntaxNodeAnalysisContext context, ExpressionSyntax value, ISymbol collection)
        {
            if (value == null) return false;
            foreach (IdentifierNameSyntax name in value.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (name.Identifier.ValueText == collection.Name && SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, collection)) return true;
            }
            return false;
        }
    }
}