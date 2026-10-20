using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6506: Service.Set<T> or Service.SetInstance<T> called outside the types allowed to register
    // services by hand, as in "Service.Set<IInventory>(this);" in an Awake.
    //
    // A manual registration replaces whatever [Service] or a Services component set up, from wherever
    // the call happens to run, so which instance clients get depends on load order. It also outlives
    // the object with domain reload off. Register with [Service] or a Services component, and keep
    // manual registration for the few places with a concrete reason, such as re-registering after
    // Enter Play Mode without a domain reload.
    //
    // The allowed types come from the allowed_service_set_types setting, as full names; a call in one
    // of them or in a type nested inside one is fine. With no setting every call is reported. Reported
    // on Set or SetInstance.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ManualServiceRegistrationAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6506",
            "Service registered by hand",
            "'{0}' registers '{1}' by hand; use [Service] or a Services component, or add this type to allowed_service_set_types if it really needs to",
            "InitArgs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ImmutableArray<string> allowed = scope.Settings.AllowedServiceSetTypes;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(invocation.Expression is MemberAccessExpressionSyntax access)) return;
                string name = access.Name.Identifier.ValueText;
                if (name != "Set" && name != "SetInstance") return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!SymbolNames.IsNamed(method.ContainingType, "Sisus.Init.Service") || method.TypeArguments.Length != 1) return;

                ISymbol enclosing = context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken);
                INamedTypeSymbol caller = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
                for (INamedTypeSymbol type = caller; type != null; type = type.ContainingType)
                {
                    foreach (string allowedType in allowed)
                    {
                        if (SymbolNames.IsNamed(type, allowedType)) return;
                    }
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.Identifier.GetLocation(), caller?.Name ?? "This code", method.TypeArguments[0].Name));
            }), SyntaxKind.InvocationExpression);
        }
    }
}