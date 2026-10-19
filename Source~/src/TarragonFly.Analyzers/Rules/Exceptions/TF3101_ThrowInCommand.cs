using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    // TF3101: a throw inside a scenario command or another type the project marks as a gameplay
    // path, as in "if (_audio == null) throw new InvalidOperationException(...)" in a PlaySfx command.
    //
    // One missing optional service then stops the whole scenario, or a cutscene, partway through,
    // and the player is left stuck. Commands should log the problem with the class prefix, set their
    // error state and carry on or return.
    //
    // The types come from the command_base_types setting: full names of base classes, such as
    // "ScenarioEngine.Runtime.CommandBase"; with no setting, the rule checks nothing. Every member of
    // those classes and the classes derived from them is checked, nested types, lambdas and local
    // functions included. Fine: "throw;", which passes on an exception from somewhere else, and
    // throwing OperationCanceledException, which is how a cancelled UniTask stops. Reported on the
    // throw keyword. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThrowInCommandAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3101",
            "Throw in a scenario command",
            "'{0}' is a {1}, which reports failures with a prefixed log and its error state instead of throwing",
            "Exceptions",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ImmutableArray<string> baseTypes = scope.Settings.CommandBaseTypes;
            if (baseTypes.IsEmpty) return;

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax thrown = context.Node is ThrowStatementSyntax statement ? statement.Expression : ((ThrowExpressionSyntax)context.Node).Expression;
                if (thrown == null) return;
                ITypeSymbol exception = context.SemanticModel.GetTypeInfo(thrown, context.CancellationToken).Type;
                if (SymbolNames.IsOrDerivesFrom(exception, "System.OperationCanceledException")) return;

                ISymbol member = context.SemanticModel.GetEnclosingSymbol(context.Node.SpanStart, context.CancellationToken);
                for (INamedTypeSymbol type = member as INamedTypeSymbol ?? member?.ContainingType; type != null; type = type.ContainingType)
                {
                    string baseType = MatchingBase(type, baseTypes);
                    if (baseType == null) continue;
                    SyntaxToken keyword = context.Node is ThrowStatementSyntax s ? s.ThrowKeyword : ((ThrowExpressionSyntax)context.Node).ThrowKeyword;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, keyword.GetLocation(), type.Name, ShortName(baseType)));
                    return;
                }
            }), SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression);
        }

        private static string MatchingBase(INamedTypeSymbol type, ImmutableArray<string> baseTypes)
        {
            foreach (string name in baseTypes)
            {
                if (SymbolNames.IsOrDerivesFrom(type, name)) return name;
            }
            return null;
        }

        private static string ShortName(string fullName)
        {
            return fullName.Substring(fullName.LastIndexOf('.') + 1);
        }
    }
}