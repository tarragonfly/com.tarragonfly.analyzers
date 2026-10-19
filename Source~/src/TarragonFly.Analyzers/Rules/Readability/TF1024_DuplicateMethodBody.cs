using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1024: two methods of the same type with the same body, as in OnPlayerHit and OnPlayerFell both
    // resetting the combo and playing the same sound.
    //
    // A fix made in one copy is easily missed in the other. Have one call the other, or merge them.
    //
    // Bodies match when they are the same code apart from comments and layout, with at least two
    // statements, and the parameters are written with the same types in the same order. Local functions
    // are compared too. Expression bodies, constructors and accessors aren't checked, nor are methods
    // in other types. Each method or local function that repeats an earlier one in the type is reported
    // on its name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DuplicateMethodBodyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1024",
            "Methods with identical bodies",
            "'{0}' has the same body as '{1}'; have one call the other, or merge them",
            "Readability",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSymbolAction(context =>
            {
                INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
                List<(SyntaxToken Name, ParameterListSyntax Parameters, BlockSyntax Body)> methods = new List<(SyntaxToken, ParameterListSyntax, BlockSyntax)>();
                foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
                {
                    if (method.MethodKind != MethodKind.Ordinary || method.DeclaringSyntaxReferences.Length != 1) continue;
                    if (!(method.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is MethodDeclarationSyntax declaration)) continue;
                    if (WorthComparing(declaration.Body)) methods.Add((declaration.Identifier, declaration.ParameterList, declaration.Body));
                    foreach (LocalFunctionStatementSyntax local in declaration.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
                    {
                        if (WorthComparing(local.Body)) methods.Add((local.Identifier, local.ParameterList, local.Body));
                    }
                }
                methods.Sort((a, b) =>
                {
                    int byFile = string.CompareOrdinal(a.Body.SyntaxTree.FilePath, b.Body.SyntaxTree.FilePath);
                    return byFile != 0 ? byFile : a.Body.SpanStart.CompareTo(b.Body.SpanStart);
                });

                for (int i = 1; i < methods.Count; i++)
                {
                    for (int j = 0; j < i; j++)
                    {
                        if (!SameParameterTypes(methods[i].Parameters, methods[j].Parameters) || !SyntaxFactory.AreEquivalent(methods[i].Body, methods[j].Body)) continue;
                        Location location = methods[i].Name.GetLocation();
                        if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, methods[i].Name.ValueText, methods[j].Name.ValueText));
                        break;
                    }
                }
            }, SymbolKind.NamedType);
        }

        // A block body of two or more statements; one-liners repeat on purpose too often to matter.
        private static bool WorthComparing(BlockSyntax body)
        {
            return body != null && body.Statements.Count >= 2;
        }

        private static bool SameParameterTypes(ParameterListSyntax first, ParameterListSyntax second)
        {
            if (first.Parameters.Count != second.Parameters.Count) return false;
            for (int i = 0; i < first.Parameters.Count; i++)
            {
                ParameterSyntax a = first.Parameters[i];
                ParameterSyntax b = second.Parameters[i];
                if (!SyntaxFactory.AreEquivalent(a.Type, b.Type) || a.Modifiers.ToString() != b.Modifiers.ToString()) return false;
            }
            return true;
        }
    }
}