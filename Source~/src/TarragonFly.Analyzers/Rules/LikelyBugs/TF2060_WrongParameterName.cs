using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2060: an ArgumentException, ArgumentNullException or other argument exception whose parameter
    // name isn't a parameter where it is thrown, as in "throw new ArgumentNullException("target")" in
    // "void Follow(Transform player)".
    //
    // The name ends up in the error message and the exception's ParamName, so a renamed or wrong name
    // sends whoever reads the log after the wrong argument. nameof() keeps it right through renames.
    //
    // The parameters of every enclosing method, constructor, operator, lambda and local function count;
    // inside a property accessor "value" and the property name do too. Only constant names are checked.
    // Reported on the "new" expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WrongParameterNameAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2060",
            "Argument exception names a parameter that doesn't exist",
            "'{0}' isn't a parameter here; pass the real parameter name, ideally with nameof()",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BaseObjectCreationExpressionSyntax creation = (BaseObjectCreationExpressionSyntax)context.Node;
                if (creation.ArgumentList == null) return;
                if (!(context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is IMethodSymbol constructor)) return;
                if (!SymbolNames.IsOrDerivesFrom(constructor.ContainingType, "System.ArgumentException")) return;

                string name = ParamNameArgument(context.SemanticModel, creation, constructor, context.CancellationToken);
                if (name == null || NamesInScope(context.SemanticModel, creation, context.CancellationToken).Contains(name)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), name));
            }), SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        }

        private static string ParamNameArgument(SemanticModel model, BaseObjectCreationExpressionSyntax creation, IMethodSymbol constructor, System.Threading.CancellationToken ct)
        {
            for (int i = 0; i < creation.ArgumentList.Arguments.Count; i++)
            {
                ArgumentSyntax argument = creation.ArgumentList.Arguments[i];
                string parameterName = argument.NameColon != null ? argument.NameColon.Name.Identifier.ValueText
                    : i < constructor.Parameters.Length ? constructor.Parameters[i].Name : null;
                if (parameterName != "paramName") continue;
                Optional<object> value = model.GetConstantValue(argument.Expression, ct);
                return value.HasValue ? value.Value as string : null;
            }
            return null;
        }

        private static HashSet<string> NamesInScope(SemanticModel model, SyntaxNode node, System.Threading.CancellationToken ct)
        {
            HashSet<string> names = new HashSet<string>();
            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax lambda:
                        AddParameters(names, model.GetSymbolInfo(lambda, ct).Symbol as IMethodSymbol);
                        break;
                    case LocalFunctionStatementSyntax _:
                    case BaseMethodDeclarationSyntax _:
                    case AccessorDeclarationSyntax _:
                        AddParameters(names, model.GetDeclaredSymbol(ancestor, ct) as IMethodSymbol);
                        break;
                    case BasePropertyDeclarationSyntax property:
                        names.Add("value");
                        if (property is PropertyDeclarationSyntax named) names.Add(named.Identifier.ValueText);
                        if (property is IndexerDeclarationSyntax indexer)
                        {
                            foreach (ParameterSyntax parameter in indexer.ParameterList.Parameters) names.Add(parameter.Identifier.ValueText);
                        }
                        break;
                }
            }
            return names;
        }

        private static void AddParameters(HashSet<string> names, IMethodSymbol method)
        {
            if (method == null) return;
            foreach (IParameterSymbol parameter in method.Parameters) names.Add(parameter.Name);
        }
    }
}