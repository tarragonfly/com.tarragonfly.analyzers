using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2067: a constant regular expression that doesn't parse, as in new Regex("[0-9") or
    // Regex.IsMatch(name, "(enemy").
    //
    // The Regex throws ArgumentException when the line first runs, which for a name filter or text
    // parser may be well into a play session.
    //
    // The Regex constructor and the static IsMatch, Match, Matches, Replace and Split count; constant
    // options are applied. The pattern is a constant, or a local or readonly field set only by its
    // constant initializer. It's checked by building a Regex from it; a pattern that parses but uses
    // something the options can't run, such as a backreference under NonBacktracking, isn't reported.
    // Reported on the pattern argument.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InvalidRegexAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2067",
            "Regular expression that doesn't parse",
            "This pattern throws when the Regex is built: {0}",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ArgumentListSyntax arguments = context.Node is InvocationExpressionSyntax invocation ? invocation.ArgumentList : ((BaseObjectCreationExpressionSyntax)context.Node).ArgumentList;
                if (arguments == null) return;
                if (!(context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!SymbolNames.IsNamed(method.ContainingType, "System.Text.RegularExpressions.Regex")) return;
                if (method.MethodKind != MethodKind.Constructor && !(method.IsStatic && IsPatternMethod(method.Name))) return;

                ExpressionSyntax patternExpression = null;
                RegexOptions options = RegexOptions.None;
                for (int i = 0; i < arguments.Arguments.Count; i++)
                {
                    ArgumentSyntax argument = arguments.Arguments[i];
                    string parameterName = argument.NameColon != null ? argument.NameColon.Name.Identifier.ValueText
                        : i < method.Parameters.Length ? method.Parameters[i].Name : null;
                    if (parameterName == "pattern") patternExpression = argument.Expression;
                    if (parameterName != "options") continue;
                    Optional<object> optionsValue = context.SemanticModel.GetConstantValue(argument.Expression, context.CancellationToken);
                    if (!optionsValue.HasValue || !(optionsValue.Value is int bits)) return;
                    options = (RegexOptions)bits;
                }
                if (patternExpression == null) return;
                string text = PatternText(context.SemanticModel, patternExpression, context.CancellationToken);
                if (text == null) return;

                string error = ParseError(text, options & ~RegexOptions.Compiled);
                if (error != null) context.ReportDiagnostic(Diagnostic.Create(Rule, patternExpression.GetLocation(), error));
            }), SyntaxKind.InvocationExpression, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        }

        // A constant, or a local or readonly field whose initializer is a constant and that nothing
        // else assigns. A field initialized in another file counts only with a plain string literal.
        private static string PatternText(SemanticModel model, ExpressionSyntax expression, CancellationToken cancellationToken)
        {
            Optional<object> constant = model.GetConstantValue(expression, cancellationToken);
            if (constant.HasValue) return constant.Value as string;

            ISymbol symbol = model.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (!(symbol is ILocalSymbol) && !(symbol is IFieldSymbol field && field.IsReadOnly)) return null;
            if (symbol.DeclaringSyntaxReferences.Length != 1) return null;
            if (!(symbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is VariableDeclaratorSyntax declarator) || declarator.Initializer == null) return null;

            string text;
            if (declarator.SyntaxTree == model.SyntaxTree)
            {
                Optional<object> initial = model.GetConstantValue(declarator.Initializer.Value, cancellationToken);
                text = initial.HasValue ? initial.Value as string : null;
            }
            else
            {
                text = declarator.Initializer.Value.IsKind(SyntaxKind.StringLiteralExpression) ? ((LiteralExpressionSyntax)declarator.Initializer.Value).Token.ValueText : null;
            }
            if (text == null) return null;

            if (symbol is ILocalSymbol)
            {
                SyntaxNode member = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>();
                return member == null || IsAssigned(member, symbol.Name, name => SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, cancellationToken).Symbol, symbol)) ? null : text;
            }
            foreach (SyntaxReference reference in symbol.ContainingType.DeclaringSyntaxReferences)
            {
                foreach (ConstructorDeclarationSyntax constructor in reference.GetSyntax(cancellationToken).ChildNodes().OfType<ConstructorDeclarationSyntax>())
                {
                    if (IsAssigned(constructor, symbol.Name, _ => true)) return null;
                }
            }
            return text;
        }

        // Whether code under the node assigns, increments or passes by ref or out a name that the
        // check accepts, bare or through "this.".
        private static bool IsAssigned(SyntaxNode node, string name, Func<IdentifierNameSyntax, bool> isTheVariable)
        {
            foreach (IdentifierNameSyntax identifier in node.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Identifier.ValueText != name) continue;
                ExpressionSyntax target = identifier.Parent is MemberAccessExpressionSyntax access && access.Name == identifier && access.Expression is ThisExpressionSyntax ? (ExpressionSyntax)access : identifier;
                bool written;
                switch (target.Parent)
                {
                    case AssignmentExpressionSyntax assignment: written = assignment.Left == target; break;
                    case PrefixUnaryExpressionSyntax prefix: written = prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression); break;
                    case PostfixUnaryExpressionSyntax postfix: written = postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression); break;
                    case ArgumentSyntax argument: written = argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword); break;
                    default: written = false; break;
                }
                if (written && isTheVariable(identifier)) return true;
            }
            return false;
        }

        private static bool IsPatternMethod(string name)
        {
            return name == "IsMatch" || name == "Match" || name == "Matches" || name == "Replace" || name == "Split";
        }

        private static string ParseError(string pattern, RegexOptions options)
        {
            try
            {
                _ = new Regex(pattern, options);
                return null;
            }
            catch (ArgumentException exception)
            {
                return exception.Message;
            }
            catch (NotSupportedException)
            {
                // The pattern parses but uses something the options can't run, such as a backreference
                // under NonBacktracking. That option doesn't exist in Unity's class library.
                return null;
            }
        }
    }
}
