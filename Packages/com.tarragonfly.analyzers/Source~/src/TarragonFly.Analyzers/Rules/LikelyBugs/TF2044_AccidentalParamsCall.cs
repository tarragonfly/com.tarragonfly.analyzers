using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2044: a call that picks a "params" overload, packing its arguments into an array, while another
    // overload taking exactly those arguments would also accept them.
    //
    // C# prefers the params overload when it matches an argument better, so Format("", null, null)
    // calls Format(string, params object[]) instead of Format(object, object, object), and the
    // other overload silently never runs.
    //
    // Only calls whose arguments get packed into a new params array are checked, and the other overload
    // must have as many parameters as there are arguments, each accepting its argument. Reported on the
    // call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AccidentalParamsCallAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2044",
            "Call picks the params overload by accident",
            "This call packs its arguments into the params overload of '{0}', although another overload takes them as they are",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation) || !PacksParams(operation)) return;

                SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
                foreach (ISymbol candidate in context.SemanticModel.GetMemberGroup(invocation.Expression, context.CancellationToken))
                {
                    if (!(candidate is IMethodSymbol other) || SymbolEqualityComparer.Default.Equals(other, operation.TargetMethod)) continue;
                    if (other.Parameters.Length != arguments.Count || (other.Parameters.Length > 0 && other.Parameters[other.Parameters.Length - 1].IsParams)) continue;
                    if (!AcceptsAll(context.SemanticModel, other, arguments)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), operation.TargetMethod.Name));
                    return;
                }
            }), SyntaxKind.InvocationExpression);
        }

        private static bool PacksParams(IInvocationOperation operation)
        {
            foreach (IArgumentOperation argument in operation.Arguments)
            {
                if (argument.ArgumentKind == ArgumentKind.ParamArray && argument.Value is IArrayCreationOperation array && array.IsImplicit) return true;
            }
            return false;
        }

        private static bool AcceptsAll(SemanticModel model, IMethodSymbol method, SeparatedSyntaxList<ArgumentSyntax> arguments)
        {
            for (int i = 0; i < arguments.Count; i++)
            {
                if (arguments[i].NameColon != null || !arguments[i].RefKindKeyword.IsKind(SyntaxKind.None)) return false;
                if (!model.ClassifyConversion(arguments[i].Expression, method.Parameters[i].Type).IsImplicit) return false;
            }
            return true;
        }
    }
}