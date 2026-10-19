using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2504: a method named Dispose that isn't the IDisposable.Dispose implementation.
    //
    // Readers and "using" statements expect Dispose to mean IDisposable; a look-alike is never called
    // by using, and its resources leak.
    //
    // Fine: overrides, implementations of any interface method, Dispose on ref structs (which "using"
    // calls by pattern), and the standard "Dispose(bool disposing)" on a type that implements
    // IDisposable. Interfaces declaring their own Dispose are reported. Reported on the method name.
    // Game code only.
    //
    // It also reports the standard Dispose(bool disposing) helper; that's left alone here.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DisposeWithoutIDisposableAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2504",
            "Dispose that isn't IDisposable.Dispose",
            "This Dispose isn't IDisposable.Dispose, so 'using' never calls it; implement IDisposable or rename it",
            "Inheritance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax declaration = (MethodDeclarationSyntax)context.Node;
                if (declaration.Identifier.ValueText != "Dispose") return;
                if (!(context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is IMethodSymbol method)) return;
                if (BaseMembers.IsOverrideOrImplementation(method) || method.ContainingType.IsRefLikeType) return;
                if (IsStandardDisposeHelper(method)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.Identifier.GetLocation()));
            }), SyntaxKind.MethodDeclaration);
        }

        // protected virtual void Dispose(bool disposing) next to IDisposable.Dispose.
        private static bool IsStandardDisposeHelper(IMethodSymbol method)
        {
            if (method.Parameters.Length != 1 || method.Parameters[0].Type.SpecialType != SpecialType.System_Boolean || !method.ReturnsVoid) return false;
            return SymbolNames.Implements(method.ContainingType, i => SymbolNames.IsNamed(i, "System.IDisposable"));
        }
    }
}