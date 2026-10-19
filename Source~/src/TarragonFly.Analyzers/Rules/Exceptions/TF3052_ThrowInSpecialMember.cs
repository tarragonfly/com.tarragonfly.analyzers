using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    // TF3052: an exception thrown from a member that callers never expect to throw: Equals,
    // GetHashCode, ToString, IDisposable.Dispose, ==, !=, an implicit conversion or a static
    // constructor.
    //
    // Collections, the debugger, string interpolation and "using" call these behind your back; a
    // throwing static constructor makes the whole type unusable for the rest of the session.
    //
    // NotImplementedException is fine (it marks unfinished work), as are rethrows and throws inside
    // lambdas. An expression body that is only a throw counts; a throw inside a ?: doesn't. Reported
    // on the throw. A throw caught by a catch (without a filter) in the same member isn't reported,
    // since nothing escapes.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThrowInSpecialMemberAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF3052",
            "Exception thrown from a member that shouldn't throw",
            "Don't throw from {0}; callers such as collections, the debugger and 'using' don't expect it",
            "Exceptions",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax thrown = context.Node is ThrowStatementSyntax statement ? statement.Expression : ((ThrowExpressionSyntax)context.Node).Expression;
                if (thrown == null) return;
                if (context.Node is ThrowExpressionSyntax && !(context.Node.Parent is ArrowExpressionClauseSyntax)) return;

                ITypeSymbol type = context.SemanticModel.GetTypeInfo(thrown, context.CancellationToken).Type;
                if (SymbolNames.IsNamed(type, "System.NotImplementedException")) return;

                SyntaxNode member = null;
                SyntaxNode child = context.Node;
                for (SyntaxNode ancestor = context.Node.Parent; ancestor != null; child = ancestor, ancestor = ancestor.Parent)
                {
                    if (ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax) return;
                    if (ancestor is TryStatementSyntax tryStatement && tryStatement.Block == child && CatchClauses.Catch(context.SemanticModel, tryStatement, type)) return;
                    if (ancestor is MemberDeclarationSyntax)
                    {
                        member = ancestor;
                        break;
                    }
                }

                string kind = SpecialMemberKind(context.SemanticModel, member);
                if (kind != null) context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), kind));
            }), SyntaxKind.ThrowStatement, SyntaxKind.ThrowExpression);
        }

        // How to name the member in the message, or null when throwing from it is fine.
        private static string SpecialMemberKind(SemanticModel model, SyntaxNode member)
        {
            switch (member)
            {
                case ConstructorDeclarationSyntax constructor when constructor.Modifiers.Any(SyntaxKind.StaticKeyword):
                    return "a static constructor";
                case OperatorDeclarationSyntax op when op.OperatorToken.IsKind(SyntaxKind.EqualsEqualsToken) || op.OperatorToken.IsKind(SyntaxKind.ExclamationEqualsToken):
                    return "operator " + op.OperatorToken.Text;
                case ConversionOperatorDeclarationSyntax conversion when conversion.ImplicitOrExplicitKeyword.IsKind(SyntaxKind.ImplicitKeyword):
                    return "an implicit conversion";
                case MethodDeclarationSyntax declaration:
                    return model.GetDeclaredSymbol(declaration) is IMethodSymbol method ? MethodKind(method) : null;
                default:
                    return null;
            }
        }

        private static string MethodKind(IMethodSymbol method)
        {
            switch (method.Name)
            {
                case "GetHashCode" when method.IsOverride && method.Parameters.Length == 0:
                case "ToString" when method.IsOverride && method.Parameters.Length == 0:
                    return method.Name;
                case "Equals" when method.Parameters.Length == 1 && (method.IsOverride || ImplementsInterface(method, "System.IEquatable`1")):
                    return "Equals";
                case "Dispose" when method.Parameters.Length == 0 && ImplementsInterface(method, "System.IDisposable"):
                    return "Dispose";
                default:
                    return null;
            }
        }

        private static bool ImplementsInterface(IMethodSymbol method, string interfaceName)
        {
            foreach (IMethodSymbol baseMethod in BaseMembers.Of(method))
            {
                if (SymbolNames.IsNamed(baseMethod.ContainingType.OriginalDefinition, interfaceName)) return true;
            }
            return false;
        }
    }
}