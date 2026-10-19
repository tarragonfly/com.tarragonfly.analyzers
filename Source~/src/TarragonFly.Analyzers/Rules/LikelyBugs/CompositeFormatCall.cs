using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // Composite format calls such as string.Format("{0} hp", health), for TF2024 and TF2053. A call
    // counts when the method has a string parameter named "format" followed by object arguments,
    // which covers string.Format, Console.Write(Line), TextWriter, StringBuilder.AppendFormat and
    // Unity's Debug.LogFormat family.
    public sealed class CompositeFormatCall
    {
        private CompositeFormatCall(string format, int argumentCount)
        {
            Format = format;
            ArgumentCount = argumentCount;
        }

        public string Format { get; }
        public int ArgumentCount { get; }

        // Null when the call isn't a composite format call with a known format string and argument count.
        public static CompositeFormatCall Read(SemanticModel model, InvocationExpressionSyntax invocation)
        {
            if (!(model.GetOperation(invocation) is IInvocationOperation operation)) return null;
            IMethodSymbol method = operation.TargetMethod;
            int formatIndex = -1;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                if (method.Parameters[i].Name == "format" && method.Parameters[i].Type.SpecialType == SpecialType.System_String) formatIndex = i;
            }
            if (formatIndex < 0 || formatIndex == method.Parameters.Length - 1) return null;

            string format = null;
            int count = 0;
            foreach (IArgumentOperation argument in operation.Arguments)
            {
                int index = argument.Parameter.Ordinal;
                if (index == formatIndex) format = ConstantString(model, argument.Value.Syntax as ExpressionSyntax);
                else if (index > formatIndex)
                {
                    if (argument.Parameter.Type.SpecialType != SpecialType.System_Object && !argument.Parameter.IsParams) return null;
                    // An array passed as is has an unknown length.
                    if (argument.Parameter.IsParams && argument.ArgumentKind != ArgumentKind.ParamArray) return null;
                    if (argument.ArgumentKind == ArgumentKind.ParamArray)
                    {
                        if (!(argument.Value is IArrayCreationOperation array) || !array.IsImplicit || array.Initializer == null) return null;
                        count += array.Initializer.ElementValues.Length;
                    }
                    else if (!argument.IsImplicit)
                    {
                        count++;
                    }
                }
            }
            return format == null ? null : new CompositeFormatCall(format, count);
        }

        // A constant, or a local initialized with a constant and never assigned again.
        private static string ConstantString(SemanticModel model, ExpressionSyntax expression)
        {
            if (expression == null) return null;
            Optional<object> constant = model.GetConstantValue(expression);
            if (constant.HasValue) return constant.Value as string;
            if (!(expression is IdentifierNameSyntax name) || !(model.GetSymbolInfo(name).Symbol is ILocalSymbol local)) return null;
            if (local.DeclaringSyntaxReferences.Length != 1 || !(local.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax declarator) || declarator.Initializer == null) return null;

            SyntaxNode body = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
            foreach (SyntaxNode node in body.DescendantNodes())
            {
                if (node is AssignmentExpressionSyntax assignment && assignment.Left is IdentifierNameSyntax target
                    && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(target).Symbol, local)) return null;
            }
            return model.GetConstantValue(declarator.Initializer.Value).Value as string;
        }

        // The placeholder indexes in order; false when the format is malformed and would throw.
        public static bool TryParse(string format, out List<int> indexes)
        {
            indexes = new List<int>();
            int i = 0;
            while (i < format.Length)
            {
                char c = format[i];
                if (c == '}')
                {
                    if (i + 1 < format.Length && format[i + 1] == '}') { i += 2; continue; }
                    return false;
                }
                if (c != '{') { i++; continue; }
                if (i + 1 < format.Length && format[i + 1] == '{') { i += 2; continue; }

                int end = format.IndexOf('}', i);
                if (end < 0) return false;
                string item = format.Substring(i + 1, end - i - 1);
                int stop = item.IndexOfAny(new[] { ',', ':' });
                string number = (stop < 0 ? item : item.Substring(0, stop)).Trim();
                if (number.Length == 0 || !int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int index)) return false;
                if (item.IndexOf('{') >= 0) return false;
                indexes.Add(index);
                i = end + 1;
            }
            return true;
        }
    }
}