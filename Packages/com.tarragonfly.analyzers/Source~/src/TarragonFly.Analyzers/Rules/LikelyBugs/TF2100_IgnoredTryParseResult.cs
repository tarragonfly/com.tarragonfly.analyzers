using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2100: the bool a TryParse method returns is thrown away, as in
    // "int.TryParse(field.value, out int count);" or "ColorUtility.TryParseHtmlString(hex, out Color tint);".
    //
    // The out value is a default (0, black, null) when the text doesn't parse, and nothing tells the
    // code that came next. A typo in a settings field or a debug window then turns into 0 volume or a
    // black tint with no warning.
    //
    // Counted: calls used as a whole statement to a method that returns bool, has an out parameter and
    // is named TryParse or TryParseExact, the .NET names for parsing text, or whose name starts with
    // "TryParse" in the System, UnityEngine or UnityEditor namespaces, such as TryParseHtmlString.
    // "_ = int.TryParse(...)" says the result is ignored on purpose and is fine. Other Try methods
    // aren't counted: "lookup.TryGetValue(id, out int count);" is the usual way to take the default
    // when a key is missing, and a project's other Try methods are its authors' call. Reported on the
    // method name.
    //
    // Covers the TryParse part of the CA1806 rule; the other results it checks are TF2019's job.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IgnoredTryParseResultAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2100",
            "Result of a TryParse call is ignored",
            "'{0}' reports whether the text parsed, and the out value is only a default when it didn't; check the result, or write '_ =' to ignore it on purpose",
            "LikelyBugs",
            CodeKind.All,
            sourceRule: "CA1806");

        private static readonly string[] PlatformNamespaces = { "System", "UnityEngine", "UnityEditor" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionStatementSyntax statement = (ExpressionStatementSyntax)context.Node;
                if (!(statement.Expression is InvocationExpressionSyntax invocation)) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!IsParse(method) || method.ReturnType.SpecialType != SpecialType.System_Boolean) return;
                if (!method.Parameters.Any(p => p.RefKind == RefKind.Out)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, NameLocation(invocation), method.Name));
            }), SyntaxKind.ExpressionStatement);
        }

        private static bool IsParse(IMethodSymbol method)
        {
            if (method.Name == "TryParse" || method.Name == "TryParseExact") return true;
            if (!method.Name.StartsWith("TryParse", StringComparison.Ordinal)) return false;
            string space = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            return PlatformNamespaces.Any(root => space == root || space.StartsWith(root + ".", StringComparison.Ordinal));
        }

        private static Location NameLocation(InvocationExpressionSyntax invocation)
        {
            switch (invocation.Expression)
            {
                case MemberAccessExpressionSyntax access: return access.Name.GetLocation();
                case MemberBindingExpressionSyntax binding: return binding.Name.GetLocation();
                default: return invocation.Expression.GetLocation();
            }
        }
    }
}