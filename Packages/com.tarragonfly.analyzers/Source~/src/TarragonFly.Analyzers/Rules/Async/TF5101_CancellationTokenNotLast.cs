using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5101: a CancellationToken parameter with ordinary parameters after it, as in
    // "UniTask FadeAsync(CancellationToken ct, float seconds)".
    //
    // Keeping the token last is the .NET and UniTask convention, so every async method reads the same
    // and a token can be passed on without looking up where it goes.
    //
    // Parameters that may follow the token: optional parameters, which can't come before a required
    // token, params arrays, out and ref parameters, and parameters with a caller-info attribute
    // ([CallerMemberName], [CallerFilePath], [CallerLineNumber], [CallerArgumentExpression]).
    // Overrides and interface implementations are fine, since their order is set elsewhere, and so is
    // an extension method whose "this" parameter is the token.
    // Methods, local functions and delegate types are checked. Constructors aren't: one that takes a
    // token usually stores it next to the other values in field order, and nobody awaits it. Reported
    // on the token parameter.
    //
    // Written from the CA1068 rule's documented behaviour.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CancellationTokenNotLastAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5101",
            "CancellationToken parameter is not last",
            "Move '{0}' to the end of the parameter list, where every async method keeps its CancellationToken",
            "Async",
            CodeKind.All,
            sourceRule: "CA1068");

        private static readonly string[] CallerInfoAttributes =
        {
            "System.Runtime.CompilerServices.CallerMemberNameAttribute",
            "System.Runtime.CompilerServices.CallerFilePathAttribute",
            "System.Runtime.CompilerServices.CallerLineNumberAttribute",
            "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                IMethodSymbol method = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) as IMethodSymbol;
                if (method == null && context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is INamedTypeSymbol type) method = type.DelegateInvokeMethod;
                if (method == null || BaseMembers.IsOverrideOrImplementation(method)) return;

                IParameterSymbol misplaced = FirstMisplacedToken(method);
                if (misplaced == null || misplaced.Locations.Length == 0) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, misplaced.Locations[0], misplaced.Name));
            }), SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.DelegateDeclaration);
        }

        // The first token that has an ordinary parameter after it.
        private static IParameterSymbol FirstMisplacedToken(IMethodSymbol method)
        {
            ImmutableArray<IParameterSymbol> parameters = method.Parameters;
            int lastOrdinary = -1;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!MayFollowToken(parameters[i])) lastOrdinary = i;
            }

            for (int i = 0; i < lastOrdinary; i++)
            {
                if (!UniTask.IsCancellationToken(parameters[i].Type)) continue;
                if (i == 0 && method.IsExtensionMethod) continue;
                return parameters[i];
            }
            return null;
        }

        private static bool MayFollowToken(IParameterSymbol parameter)
        {
            if (parameter.IsOptional || parameter.IsParams || parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref) return true;
            if (UniTask.IsCancellationToken(parameter.Type)) return true;
            foreach (string attribute in CallerInfoAttributes)
            {
                if (SymbolNames.HasAttribute(parameter, attribute)) return true;
            }
            return false;
        }
    }
}