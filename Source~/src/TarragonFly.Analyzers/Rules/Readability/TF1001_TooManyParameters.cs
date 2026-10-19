using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1001: a method, constructor, local function, lambda, anonymous method or delegate with more
    // parameters than max_parameters (default 7).
    //
    // Long parameter lists are easy to call in the wrong order and usually mean the values belong
    // together in a struct or settings object.
    //
    // Skipped, since their signature is fixed elsewhere: overrides, interface implementations, extern
    // methods and Init(args) Init(...) methods. Operators, indexers and record parameter lists aren't
    // checked. Reported on the parameter list.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TooManyParametersAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1001",
            "Too many parameters",
            "This has {0} parameters, more than the limit of {1}; group related values into a struct or settings object",
            "Readability",
            CodeKind.All,
            description: "The limit is max_parameters in the settings file.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            int limit = scope.Settings.MaxParameters;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ParameterListSyntax parameters = Parameters(context.Node);
                if (parameters == null || parameters.Parameters.Count <= limit || HasFixedSignature(context)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, parameters.GetLocation(), parameters.Parameters.Count, limit));
            }), SyntaxKind.MethodDeclaration, SyntaxKind.ConstructorDeclaration, SyntaxKind.LocalFunctionStatement,
                SyntaxKind.ParenthesizedLambdaExpression, SyntaxKind.AnonymousMethodExpression, SyntaxKind.DelegateDeclaration);
        }

        private static ParameterListSyntax Parameters(SyntaxNode node)
        {
            switch (node)
            {
                case BaseMethodDeclarationSyntax method: return method.ParameterList;
                case LocalFunctionStatementSyntax local: return local.ParameterList;
                case ParenthesizedLambdaExpressionSyntax lambda: return lambda.ParameterList;
                case AnonymousMethodExpressionSyntax anonymous: return anonymous.ParameterList;
                case DelegateDeclarationSyntax type: return type.ParameterList;
                default: return null;
            }
        }

        private static bool HasFixedSignature(SyntaxNodeAnalysisContext context)
        {
            if (!(context.Node is MethodDeclarationSyntax declaration)) return false;
            foreach (SyntaxToken modifier in declaration.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.ExternKeyword)) return true;
            }
            IMethodSymbol method = context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken);
            return method != null && (BaseMembers.IsOverrideOrImplementation(method) || InitArgs.IsInitMethod(method));
        }
    }
}