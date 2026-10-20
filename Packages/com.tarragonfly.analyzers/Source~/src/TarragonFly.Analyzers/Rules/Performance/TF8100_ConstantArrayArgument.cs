using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8100: an array of constants built inside a method and passed straight to a call, as in
    // "line.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)".
    //
    // The same array is allocated on every call, which shows up as garbage collector spikes when the
    // method runs every frame or for every item. When the method called doesn't change the array,
    // build it once in a static readonly field and pass that.
    //
    // Counted: "new[] { ... }" and "new T[] { ... }" with at least one item, every item a constant
    // (literals, const fields, enum members, nameof), passed as an argument inside a method, accessor,
    // local function or lambda. Not counted: params parameters (TF8006 says to pass the items
    // directly), Span and ReadOnlySpan parameters, which the compiler can fill without allocating,
    // constructor arguments, as in "new HashSet<string>(new[] { ... })", where the new object costs
    // more than the array and usually keeps or copies it, static constructors and field initializers,
    // which run once, and attribute arguments. Reported on the array. Game code only.
    //
    // Written from the CA1861 rule's documented behaviour.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ConstantArrayArgumentAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8100",
            "Constant array built on every call",
            "This array of constants is allocated on every call; if '{0}' doesn't change it, move it to a static readonly field",
            "Performance",
            CodeKind.Game,
            sourceRule: "CA1861");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ArgumentSyntax argument = (ArgumentSyntax)context.Node;
                InitializerExpressionSyntax initializer = argument.Expression is ArrayCreationExpressionSyntax creation ? creation.Initializer
                    : argument.Expression is ImplicitArrayCreationExpressionSyntax implicitCreation ? implicitCreation.Initializer : null;
                if (initializer == null || initializer.Expressions.Count == 0 || RunsOnce(argument)) return;
                if (!(context.SemanticModel.GetOperation(argument, context.CancellationToken) is IArgumentOperation operation)) return;
                if (operation.Parameter == null || operation.Parameter.IsParams || IsSpan(operation.Parameter.Type)) return;
                if (operation.Parent is IObjectCreationOperation) return;
                if (!initializer.Expressions.All(item => context.SemanticModel.GetConstantValue(item, context.CancellationToken).HasValue)) return;

                string called = (operation.Parent as IInvocationOperation)?.TargetMethod.Name ?? operation.Parameter.ContainingSymbol.Name;
                context.ReportDiagnostic(Diagnostic.Create(Rule, argument.Expression.GetLocation(), called));
            }), SyntaxKind.Argument);
        }

        // Static constructors and field initializers; anything inside a lambda or local function there
        // still runs every time it is called.
        private static bool RunsOnce(SyntaxNode node)
        {
            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                if (ancestor is AnonymousFunctionExpressionSyntax || ancestor is LocalFunctionStatementSyntax) return false;
                if (ancestor is ConstructorDeclarationSyntax constructor) return constructor.Modifiers.Any(SyntaxKind.StaticKeyword);
                if (ancestor is BaseMethodDeclarationSyntax || ancestor is AccessorDeclarationSyntax || ancestor is ArrowExpressionClauseSyntax) return false;
                if (ancestor is FieldDeclarationSyntax || ancestor is PropertyDeclarationSyntax || ancestor is EventFieldDeclarationSyntax) return true;
            }
            return true;
        }

        private static bool IsSpan(ITypeSymbol type)
        {
            return SymbolNames.IsNamed(type?.OriginalDefinition, "System.Span`1") || SymbolNames.IsNamed(type?.OriginalDefinition, "System.ReadOnlySpan`1");
        }
    }
}