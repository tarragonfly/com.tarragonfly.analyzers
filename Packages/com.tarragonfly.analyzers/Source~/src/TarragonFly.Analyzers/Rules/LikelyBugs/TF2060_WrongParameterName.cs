using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2060: an ArgumentException, ArgumentNullException or other argument exception whose parameter
    // name isn't a parameter where it is thrown, as in "throw new ArgumentNullException("target")" in
    // "void Follow(Transform player)", or that names no parameter, as in a bare
    // "throw new ArgumentOutOfRangeException()".
    //
    // The name ends up in the error message and the exception's ParamName, so a missing, renamed or
    // wrong name sends whoever reads the log after the wrong argument. nameof() keeps it right through
    // renames.
    //
    // The parameters of every enclosing method, constructor, operator, lambda and local function count;
    // inside a property accessor "value" and the property name do too. Only constant names are checked,
    // and a null name counts as none. The parameterless constructors of ArgumentException,
    // ArgumentNullException, ArgumentOutOfRangeException and DuplicateWaitObjectException are reported
    // wherever they're used. Other argument exceptions aren't, since they may have no constructor that
    // takes a name. When a parameterless one is thrown straight from a case, default or arm of a switch
    // on a field or anything else that isn't a parameter, there's no argument to name, so the message
    // says to throw InvalidOperationException instead. One inside an if in that case still gets the
    // usual message, since it's checking an argument. Reported on the "new" expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WrongParameterNameAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2060",
            "Argument exception without the right parameter name",
            "{0}",
            "LikelyBugs",
            CodeKind.All);

        private const string NoNameMessage = "This doesn't say which parameter is wrong; pass its name, ideally with nameof()";

        private const string NotAnArgumentMessage = "This switch isn't on a parameter, so no argument is wrong; throw InvalidOperationException instead";

        private static readonly string[] TypesTakingAName =
        {
            "System.ArgumentException", "System.ArgumentNullException", "System.ArgumentOutOfRangeException", "System.DuplicateWaitObjectException",
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BaseObjectCreationExpressionSyntax creation = (BaseObjectCreationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is IMethodSymbol constructor)) return;
                if (!SymbolNames.IsOrDerivesFrom(constructor.ContainingType, "System.ArgumentException")) return;

                if (constructor.Parameters.IsEmpty)
                {
                    if (!TypesTakingAName.Any(type => SymbolNames.IsNamed(constructor.ContainingType, type))) return;
                    string message = RejectsSwitchOnNoParameter(context.SemanticModel, creation, context.CancellationToken) ? NotAnArgumentMessage : NoNameMessage;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), message));
                    return;
                }
                if (!TryGetParamName(context.SemanticModel, creation, constructor, context.CancellationToken, out string name)) return;
                if (name == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), NoNameMessage));
                    return;
                }
                if (NamesInScope(context.SemanticModel, creation, context.CancellationToken).Contains(name)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), $"'{name}' isn't a parameter here; pass the real parameter name, ideally with nameof()"));
            }), SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
        }

        // The paramName argument when it's a constant, which may be null.
        private static bool TryGetParamName(SemanticModel model, BaseObjectCreationExpressionSyntax creation, IMethodSymbol constructor, System.Threading.CancellationToken ct, out string name)
        {
            name = null;
            if (creation.ArgumentList == null) return false;
            for (int i = 0; i < creation.ArgumentList.Arguments.Count; i++)
            {
                ArgumentSyntax argument = creation.ArgumentList.Arguments[i];
                string parameterName = argument.NameColon != null ? argument.NameColon.Name.Identifier.ValueText
                    : i < constructor.Parameters.Length ? constructor.Parameters[i].Name : null;
                if (parameterName != "paramName") continue;
                Optional<object> value = model.GetConstantValue(argument.Expression, ct);
                name = value.HasValue ? value.Value as string : null;
                return value.HasValue;
            }
            return false;
        }

        // Thrown straight from a switch section or arm whose switch value reads no parameter, as in
        // "switch (_state) { ... default: throw new ArgumentOutOfRangeException(); }". A throw nested
        // deeper, as in "case 0: if (title == null) throw ...", is checking an argument.
        private static bool RejectsSwitchOnNoParameter(SemanticModel model, SyntaxNode creation, System.Threading.CancellationToken ct)
        {
            ExpressionSyntax value = SwitchValueRejectedBy(creation.Parent);
            if (value == null) return false;
            foreach (IdentifierNameSyntax identifier in value.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
            {
                if (model.GetSymbolInfo(identifier, ct).Symbol is IParameterSymbol) return false;
            }
            return true;
        }

        private static ExpressionSyntax SwitchValueRejectedBy(SyntaxNode throwNode)
        {
            if (throwNode is ThrowExpressionSyntax && throwNode.Parent is SwitchExpressionArmSyntax arm && arm.Expression == throwNode)
            {
                return ((SwitchExpressionSyntax)arm.Parent).GoverningExpression;
            }
            if (!(throwNode is ThrowStatementSyntax)) return null;
            SyntaxNode section = throwNode.Parent is BlockSyntax block ? block.Parent : throwNode.Parent;
            return section is SwitchSectionSyntax && section.Parent is SwitchStatementSyntax switchStatement ? switchStatement.Expression : null;
        }

        private static HashSet<string> NamesInScope(SemanticModel model, SyntaxNode node, System.Threading.CancellationToken ct)
        {
            HashSet<string> names = new HashSet<string>();
            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax lambda:
                        AddParameters(names, model.GetSymbolInfo(lambda, ct).Symbol as IMethodSymbol);
                        break;
                    case LocalFunctionStatementSyntax _:
                    case BaseMethodDeclarationSyntax _:
                    case AccessorDeclarationSyntax _:
                        AddParameters(names, model.GetDeclaredSymbol(ancestor, ct) as IMethodSymbol);
                        break;
                    case BasePropertyDeclarationSyntax property:
                        names.Add("value");
                        if (property is PropertyDeclarationSyntax named) names.Add(named.Identifier.ValueText);
                        if (property is IndexerDeclarationSyntax indexer)
                        {
                            foreach (ParameterSyntax parameter in indexer.ParameterList.Parameters) names.Add(parameter.Identifier.ValueText);
                        }
                        break;
                }
            }
            return names;
        }

        private static void AddParameters(HashSet<string> names, IMethodSymbol method)
        {
            if (method == null) return;
            foreach (IParameterSymbol parameter in method.Parameters) names.Add(parameter.Name);
        }
    }
}