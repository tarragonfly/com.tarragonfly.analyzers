using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.SilentFailures
{
    // TF3002: a method or property that returns a collection but hands back null, as in "if (!loaded)
    // return null;" in "List<Item> GetLoot()".
    //
    // Callers write "foreach (Item item in GetLoot())" and get a NullReferenceException the first
    // time nothing dropped. Return an empty collection (Array.Empty<T>(), new List<T>()) instead.
    //
    // Arrays and every type that is IEnumerable count, except string and Unity's Transform, which
    // enumerates its children but is no collection. Returned null and default literals count, also as
    // a branch of "?:", in methods, property getters and expression bodies of any visibility. Struct
    // collections, such as a NativeArray, whose default isn't null, and return types marked nullable
    // with "?" under #nullable enable aren't checked; nor are lambdas and local functions. Reported on
    // the null. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NullCollectionReturnedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3002",
            "Null returned instead of an empty collection",
            "Return an empty collection instead of null; callers that loop over '{0}' will throw",
            "SilentFailures",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax returned = context.Node is ReturnStatementSyntax statement ? statement.Expression : ((ArrowExpressionClauseSyntax)context.Node).Expression;
                if (returned == null || context.Node.Parent is LocalFunctionStatementSyntax) return;
                if (!(context.SemanticModel.GetEnclosingSymbol(returned.SpanStart, context.CancellationToken) is IMethodSymbol member)) return;
                if (member.MethodKind == MethodKind.AnonymousFunction || member.MethodKind == MethodKind.LocalFunction) return;
                if (!IsCollection(member.ReturnType) || member.ReturnType.IsValueType || member.ReturnNullableAnnotation == NullableAnnotation.Annotated) return;
                ReportNulls(context, returned, member);
            }), SyntaxKind.ReturnStatement, SyntaxKind.ArrowExpressionClause);
        }

        private static void ReportNulls(SyntaxNodeAnalysisContext context, ExpressionSyntax returned, IMethodSymbol member)
        {
            switch (returned)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    ReportNulls(context, parenthesized.Expression, member);
                    break;
                case ConditionalExpressionSyntax conditional:
                    ReportNulls(context, conditional.WhenTrue, member);
                    ReportNulls(context, conditional.WhenFalse, member);
                    break;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) || literal.IsKind(SyntaxKind.DefaultLiteralExpression):
                case DefaultExpressionSyntax _:
                    string name = member.AssociatedSymbol?.Name ?? member.Name;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, returned.GetLocation(), name));
                    break;
            }
        }

        private static bool IsCollection(ITypeSymbol type)
        {
            if (type == null || type.SpecialType == SpecialType.System_String || SymbolNames.IsOrDerivesFrom(type, "UnityEngine.Transform")) return false;
            return type is IArrayTypeSymbol || SymbolNames.Implements(type, candidate => candidate.SpecialType == SpecialType.System_Collections_IEnumerable);
        }
    }
}