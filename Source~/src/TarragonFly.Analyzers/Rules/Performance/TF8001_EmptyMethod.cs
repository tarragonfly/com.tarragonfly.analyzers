using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8001: a method, local function or accessor with an empty body and no comment saying why.
    //
    // An empty Unity message is worse than dead code: Unity still calls an empty Update every frame
    // on every instance. Elsewhere it's either unfinished or deliberately does nothing, and a comment
    // should say which. (TF4001 also reports empty Unity messages, on their own.)
    //
    // Fine: a comment or #if-disabled code between the braces, constructors, destructors and
    // operators, virtual and abstract methods, interface implementations, overrides of abstract
    // methods, and the GetResult() of an awaiter (an INotifyCompletion), which await calls even when
    // there's nothing to return. Overrides of virtual methods are reported in game code; in test code they're skipped,
    // since test base classes often have empty hooks. Accessors follow the same rules, except that
    // overrides of virtual properties are reported in test code too. Reported on the name, or on the
    // accessor keyword.
    // The warning names the Unity message when the empty method is one.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EmptyMethodAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8001",
            "Empty method",
            "{0}",
            "Performance",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            bool testCode = scope.AssemblyKind == CodeKind.Tests;
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (!IsEmpty(declaration.Body)) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                if (method.IsVirtual || method.IsAbstract) return;
                if (method.IsOverride && (testCode || method.OverriddenMethod?.IsAbstract == true)) return;
                if (!method.IsOverride && BaseMembers.IsOverrideOrImplementation(method)) return;
                if (method.Name == "GetResult" && method.Parameters.IsEmpty
                    && SymbolNames.Implements(method.ContainingType, contract => SymbolNames.IsNamed(contract, "System.Runtime.CompilerServices.INotifyCompletion"))) return;

                string message = method.ContainingType != null && new ScriptInfo(method.ContainingType).IsMessage(method)
                    ? $"Unity still calls the empty {method.Name} message on every instance; remove it"
                    : "Add the missing code, or a comment saying why this does nothing";
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation(), message));
            }), SyntaxKind.MethodDeclaration);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                LocalFunctionStatementSyntax local = (LocalFunctionStatementSyntax)context.Node;
                if (IsEmpty(local.Body)) context.ReportDiagnostic(Diagnostic.Create(Rule, local.Identifier.GetLocation(), "Add the missing code, or a comment saying why this does nothing"));
            }), SyntaxKind.LocalFunctionStatement);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AccessorDeclarationSyntax accessor = (AccessorDeclarationSyntax)context.Node;
                if (!IsEmpty(accessor.Body)) return;
                if (accessor.Parent?.Parent is BasePropertyDeclarationSyntax property
                    && context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is IPropertySymbol symbol
                    && (symbol.IsVirtual || symbol.IsAbstract || symbol.OverriddenProperty?.IsAbstract == true || (!symbol.IsOverride && BaseMembers.IsOverrideOrImplementation(symbol)))) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, accessor.Keyword.GetLocation(), "Add the missing code, or a comment saying why this does nothing"));
            }), SyntaxKind.GetAccessorDeclaration, SyntaxKind.SetAccessorDeclaration, SyntaxKind.InitAccessorDeclaration);
        }

        private static bool IsEmpty(BlockSyntax body)
        {
            return body != null && body.Statements.Count == 0 && Braces.HaveOnlyWhitespaceBetween(body.OpenBraceToken, body.CloseBraceToken);
        }
    }
}