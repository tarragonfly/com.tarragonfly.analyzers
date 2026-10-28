using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.DependencyInjection
{
    // TF6504: Any<T>.Value read inside a component, as in "_audio.Value.PlaySfx(hit)" for
    // "[SerializeField] Any<IAudioService> _audio;", or the same field converted to T without .Value.
    //
    // Value resolves the reference without knowing which object asks, so services that a Services
    // component or a Service Tag gives only to this object, and client-specific value providers, are
    // missed. GetValue(this) passes the component as the client and finds them. Plain C# objects, such
    // as scenario commands, have no component to pass and may use Value for global services.
    //
    // Checked: code in Component types and the types nested in them. Wrapping a value the other way,
    // as in "Any<AudioManager> audio = audioManager;", is fine. Reported on "Value", or on the
    // converted expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AnyValueInComponentAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF6504",
            "Any<T>.Value used in a component",
            "Call GetValue(this) on '{0}' so services given to this object are found too",
            "InitArgs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterOperationAction(context =>
            {
                if (!scope.IsAnalyzed(context.Operation.Syntax.SyntaxTree) || !InComponent(context.ContainingSymbol)) return;
                if (NameOfExpressions.IsInsideNameOf(context.Operation)) return;
                switch (context.Operation)
                {
                    case IPropertyReferenceOperation property when property.Property.Name == "Value" && !property.Property.IsStatic
                        && Frameworks.InitArgs.IsAny(property.Property.ContainingType):
                        Location where = property.Syntax is MemberAccessExpressionSyntax access ? access.Name.GetLocation() : property.Syntax.GetLocation();
                        context.ReportDiagnostic(Diagnostic.Create(Rule, where, Describe(property.Instance)));
                        break;
                    // Only Any<T> to T reads the value; T to Any<T> just wraps one.
                    case IConversionOperation conversion when conversion.OperatorMethod != null
                        && Frameworks.InitArgs.IsAny(conversion.Operand.Type) && !Frameworks.InitArgs.IsAny(conversion.Type):
                        context.ReportDiagnostic(Diagnostic.Create(Rule, conversion.Operand.Syntax.GetLocation(), Describe(conversion.Operand)));
                        break;
                }
            }, OperationKind.PropertyReference, OperationKind.Conversion);
        }

        private static string Describe(IOperation instance)
        {
            return instance?.Syntax.ToString() ?? "the Any<T>";
        }

        private static bool InComponent(ISymbol enclosing)
        {
            for (INamedTypeSymbol type = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; type != null; type = type.ContainingType)
            {
                if (SymbolNames.IsOrDerivesFrom(type, "UnityEngine.Component")) return true;
            }
            return false;
        }
    }
}
