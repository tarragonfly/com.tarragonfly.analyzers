using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6503: Service<T>.Instance read inside a MonoBehaviour, as in
    // "Service<IAudioService>.Instance.PlaySfx(hit);".
    //
    // Instance is the global registration only: it skips the services a Services component or a
    // Service Tag gives this object, it is null before Init(args) has registered the service, and it
    // hides the dependency from the Inspector and from tests. Receive the service through
    // MonoBehaviour<T...> or Init(...), hold it in an Any<T> field and call GetValue(this), or look it
    // up with Service.TryGetFor(this, out T service) where it is optional.
    //
    // Checked: code in MonoBehaviour types and the types nested in them. Reported on "Instance".
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ServiceInstanceInMonoBehaviourAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6503",
            "Service<T>.Instance used in a MonoBehaviour",
            "Receive '{0}' through Init(...), an Any<T> field with GetValue(this), or Service.TryGetFor(this, ...) instead of Service<T>.Instance",
            "InitArgs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MemberAccessExpressionSyntax access = (MemberAccessExpressionSyntax)context.Node;
                if (access.Name.Identifier.ValueText != "Instance") return;
                ISymbol member = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol;
                if (!(member is IFieldSymbol || member is IPropertySymbol) || !member.IsStatic) return;
                if (!(member.ContainingType is INamedTypeSymbol service) || !SymbolNames.IsGenericNamed(service, "Sisus.Init", "Service")) return;
                if (!InMonoBehaviour(context.SemanticModel.GetEnclosingSymbol(access.SpanStart, context.CancellationToken))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, access.Name.GetLocation(), service.TypeArguments[0].Name));
            }), SyntaxKind.SimpleMemberAccessExpression);
        }

        private static bool InMonoBehaviour(ISymbol enclosing)
        {
            for (INamedTypeSymbol type = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; type != null; type = type.ContainingType)
            {
                if (Frameworks.Unity.IsMonoBehaviour(type)) return true;
            }
            return false;
        }
    }
}