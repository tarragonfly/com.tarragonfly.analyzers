using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Culture
{
    // TF3103: a number or date turned into text without saying which culture to write it in, as in
    // "speed.ToString("F2")" or "string.Format("{0}", speed)" written to a save file.
    //
    // On a German PC 1.5 comes out as "1,50", so the text no longer parses back on another machine.
    // Pass CultureInfo.InvariantCulture, or CultureInfo.CurrentCulture when the text is for the player.
    //
    // Checked: ToString with arguments on float, double, decimal, DateTime and DateTimeOffset;
    // Convert.ToString given one of those types; string.Format and StringBuilder.AppendFormat given
    // one of those values; all with no IFormatProvider or a null one. ToString() without arguments is
    // TF3500's job. Interpolated strings and Debug.LogFormat aren't checked: they mostly build UI text
    // and logs, where the player's culture is what you want. Integer formats such as "N0" aren't
    // checked either. Reported on the method name. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NumberOrDateFormattedWithoutCultureAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3103",
            "Number or date turned into text without a culture",
            "'{0}' writes the value in the player's culture, so 1.5 can come out as \"1,5\"; pass CultureInfo.InvariantCulture, or CultureInfo.CurrentCulture when the text is for the player",
            "Culture",
            CodeKind.Game,
            sourceRule: "CA1305");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                SimpleNameSyntax name = invocation.Expression is MemberAccessExpressionSyntax access ? access.Name
                    : invocation.Expression is MemberBindingExpressionSyntax binding ? binding.Name : null;
                if (name == null) return;
                string methodName = name.Identifier.ValueText;
                if (methodName != "ToString" && methodName != "Format" && methodName != "AppendFormat") return;
                if (!(context.SemanticModel.GetOperation(invocation, context.CancellationToken) is IInvocationOperation operation)) return;
                if (!FormatsNumberOrDate(operation) || CultureArguments.PassesFormatProvider(operation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation(), methodName));
            }), SyntaxKind.InvocationExpression);
        }

        private static bool FormatsNumberOrDate(IInvocationOperation operation)
        {
            IMethodSymbol method = operation.TargetMethod;
            if (method.Name == "ToString")
            {
                if (SymbolNames.IsNamed(method.ContainingType, "System.Convert")) return method.Parameters.Length > 0 && IsNumberOrDate(method.Parameters[0].Type);
                return !method.IsStatic && method.Parameters.Length > 0 && IsNumberOrDate(method.ContainingType);
            }
            bool formatsText = (method.ContainingType.SpecialType == SpecialType.System_String && method.Name == "Format")
                || (SymbolNames.IsNamed(method.ContainingType, "System.Text.StringBuilder") && method.Name == "AppendFormat");
            return formatsText && HasNumberOrDateArgument(operation);
        }

        // The values after the format string, including the ones packed into a params array.
        private static bool HasNumberOrDateArgument(IInvocationOperation operation)
        {
            foreach (IArgumentOperation argument in operation.Arguments)
            {
                if (argument.ArgumentKind == ArgumentKind.ParamArray && argument.Value is IArrayCreationOperation array && array.Initializer != null)
                {
                    foreach (IOperation element in array.Initializer.ElementValues)
                    {
                        if (IsNumberOrDate(TypeBeforeBoxing(element))) return true;
                    }
                }
                else if (argument.Parameter.Type.SpecialType == SpecialType.System_Object && IsNumberOrDate(TypeBeforeBoxing(argument.Value)))
                {
                    return true;
                }
            }
            return false;
        }

        private static ITypeSymbol TypeBeforeBoxing(IOperation value)
        {
            while (value is IConversionOperation conversion && conversion.IsImplicit) value = conversion.Operand;
            return value.Type;
        }

        private static bool IsNumberOrDate(ITypeSymbol type)
        {
            if (type == null) return false;
            switch (type.SpecialType)
            {
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_DateTime:
                    return true;
                default:
                    return SymbolNames.IsNamed(type, "System.DateTimeOffset");
            }
        }
    }
}