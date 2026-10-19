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
    // Swapped coordinates, width and height, or source and target compile fine and fail quietly.
    //
    // Reported when at least two positional arguments are simple names that match the name of a
    // different parameter. Named arguments are fine. Delegate calls aren't checked. Reported on the
    // method name (or the type for "new").
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

                int misplaced = 0;
                for (int i = 0; i < arguments.Arguments.Count && i < method.Parameters.Length; i++)
                {
                    ArgumentSyntax argument = arguments.Arguments[i];
                    if (argument.NameColon != null || !(argument.Expression is IdentifierNameSyntax identifier)) continue;
                    string text = identifier.Identifier.ValueText;
                    for (int j = 0; j < method.Parameters.Length; j++)
                    {
                        if (j != i && method.Parameters[j].Name == text && method.Parameters[i].Name != text) misplaced++;
                    }
                }
                if (misplaced >= 2) context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), method.MethodKind == MethodKind.Constructor ? method.ContainingType.Name : method.Name));
            }), SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression);
        }
    }
}