using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2021: a call whose arguments are variables named like the method's parameters, but passed in a
    // different order, as in Move(y, x) for Move(int x, int y).
    //
    // Swapped coordinates, width and height, or source and target compile fine and give wrong
    // results with no error.
    //
    // An argument is out of place when it's a simple name matching a different parameter's name.
    // Reported when an out-of-place argument belongs where another out-of-place argument sits, as when
    // two arguments trade places or several move along together, as in Place(w, x, y) for Place(x, y,
    // z). The same variable passed to several parameters, as in
    // Draw(style, style, style), or out-of-place arguments landing on unrelated ones, as in
    // Copy(buffer, source, other, target) for Copy(source, sourceStart, target, targetStart), aren't
    // reported. Named arguments are fine. Delegate calls aren't checked. Reported on the method name
    // (or the type for "new").
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SwappedArgumentsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2021",
            "Arguments passed in swapped order",
            "Arguments are named like other parameters of '{0}'; check they aren't swapped",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxNode name;
                ArgumentListSyntax arguments;
                switch (context.Node)
                {
                    case InvocationExpressionSyntax invocation:
                        name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name : invocation.Expression;
                        arguments = invocation.ArgumentList;
                        break;
                    case ObjectCreationExpressionSyntax creation:
                        name = creation.Type;
                        arguments = creation.ArgumentList;
                        break;
                    default:
                        return;
                }
                if (arguments == null || arguments.Arguments.Count < 2) return;
                if (!(context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is IMethodSymbol method) || method.MethodKind == MethodKind.DelegateInvoke) return;

                int count = System.Math.Min(arguments.Arguments.Count, method.Parameters.Length);
                int[] belongsAt = new int[count];
                for (int i = 0; i < count; i++) belongsAt[i] = BelongsAt(arguments.Arguments[i], method, i);
                for (int i = 0; i < count; i++)
                {
                    int other = belongsAt[i];
                    if (other >= 0 && other < count && belongsAt[other] >= 0)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name));
                        return;
                    }
                }
            }), SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression);
        }

        // The position of the other parameter this positional argument is named like, or -1.
        private static int BelongsAt(ArgumentSyntax argument, IMethodSymbol method, int position)
        {
            if (argument.NameColon != null || !(argument.Expression is IdentifierNameSyntax identifier)) return -1;
            string text = identifier.Identifier.ValueText;
            if (method.Parameters[position].Name == text) return -1;
            for (int j = 0; j < method.Parameters.Length; j++)
            {
                if (method.Parameters[j].Name == text) return j;
            }
            return -1;
        }
    }
}