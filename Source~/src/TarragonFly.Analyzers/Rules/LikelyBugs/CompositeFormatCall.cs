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
                    else if (argument.ArgumentKind != ArgumentKind.DefaultValue)
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
                ExpressionSyntax target = node is AssignmentExpressionSyntax assignment ? assignment.Left
                    : node is ArgumentSyntax argument && !argument.RefKindKeyword.IsKind(SyntaxKind.None) && !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword) ? argument.Expression : null;
                if (target is IdentifierNameSyntax written && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(written).Symbol, local)) return null;
            }
            return model.GetConstantValue(declarator.Initializer.Value).Value as string;
        }

        // The placeholder indexes in order; false when the format is malformed and would throw. Items
        // are read the way String.Format reads them: "{", digits right away, spaces, an optional
        // alignment of "," spaces, an optional "-" and digits, spaces, then an optional ":" format in
        // which braces must be doubled, and "}".
        public static bool TryParse(string format, out List<int> indexes)
        {
            indexes = new List<int>();
            int position = 0;
            while (position < format.Length)
            {
                char c = format[position];
                if (c == '}')
                {
                    if (position + 1 < format.Length && format[position + 1] == '}') { position += 2; continue; }
                    return false;
                }
                if (c != '{') { position++; continue; }
                if (position + 1 < format.Length && format[position + 1] == '{') { position += 2; continue; }

                position++;
                if (!IsDigitAt(format, position)) return false;
                int index = 0;
                while (IsDigitAt(format, position) && index < 1000000) index = index * 10 + (format[position++] - '0');
                SkipSpaces(format, ref position);
                if (position < format.Length && format[position] == ',')
                {
                    position++;
                    SkipSpaces(format, ref position);
                    if (position < format.Length && format[position] == '-') position++;
                    if (!IsDigitAt(format, position)) return false;
                    while (IsDigitAt(format, position)) position++;
                    SkipSpaces(format, ref position);
                }
                if (position < format.Length && format[position] == ':' && !SkipItemFormat(format, ref position)) return false;
                if (position >= format.Length || format[position] != '}') return false;
                indexes.Add(index);
                position++;
            }
            return true;
        }

        // Moves past ":format" to its closing "}"; false for a lone "{" inside, which throws.
        private static bool SkipItemFormat(string format, ref int position)
        {
            position++;
            while (position < format.Length)
            {
                char c = format[position];
                bool doubled = position + 1 < format.Length && format[position + 1] == c;
                if (c == '{' && !doubled) return false;
                if (c == '}' && !doubled) return true;
                position += c == '{' || c == '}' ? 2 : 1;
            }
            return false;
        }

        private static bool IsDigitAt(string text, int position)
        {
            return position < text.Length && text[position] >= '0' && text[position] <= '9';
        }

        private static void SkipSpaces(string text, ref int position)
        {
            while (position < text.Length && text[position] == ' ') position++;
        }
    }
}