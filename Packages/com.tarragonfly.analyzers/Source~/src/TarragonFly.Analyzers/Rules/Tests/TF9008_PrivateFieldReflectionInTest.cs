using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Tests
{
    // TF9008: a test reaching into a private field by reflection, as in
    // "typeof(Board).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(board, 8);".
    //
    // Each test that does this carries its own copy of the flags and the field-name string, and
    // a rename in the game breaks every one of them with a NullReferenceException instead of a
    // compile error. TestBench's Serialized.Set writes the field through Unity's serialization and
    // PrivateField.Set keeps the one reflection call in the shared test assembly.
    //
    // Checked in test assemblies: GetField on a System.Type with BindingFlags.NonPublic among its
    // arguments, and SetValue or GetValue on a FieldInfo, unless that FieldInfo comes straight from
    // a GetField already reported on the same line. Left alone: types whose name ends in Fakes,
    // where a fake may need to poke at the real object, and every assembly test_support_assemblies
    // lists, since that's where the one shared helper lives. Reported on the called method.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PrivateFieldReflectionInTestAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF9008",
            "Reflection into a private field in a test",
            "Set the field through Serialized.Set or PrivateField.Set from the shared test assembly instead of reflecting here",
            "Tests",
            CodeKind.Tests,
            description: "Types named *Fakes and the assemblies test_support_assemblies lists are left alone.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            if (Glob.MatchesAny(scope.Settings.TestSupportAssemblies, start.Compilation.AssemblyName)) return;

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                if (!IsPrivateFieldLookup(invocation, method, context.SemanticModel, context.CancellationToken) && !IsFieldValueAccess(invocation, method, context.SemanticModel, context.CancellationToken)) return;
                if (IsInFakes(context.SemanticModel.GetEnclosingSymbol(invocation.SpanStart, context.CancellationToken))) return;
                Location location = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.GetLocation() ?? invocation.Expression.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(Rule, location));
            }), SyntaxKind.InvocationExpression);
        }

        // Type.GetField(...) with BindingFlags.NonPublic somewhere in its arguments.
        private static bool IsPrivateFieldLookup(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model, System.Threading.CancellationToken ct)
        {
            if (method.Name != "GetField" || !SymbolNames.IsOrDerivesFrom(method.ContainingType, "System.Type")) return false;
            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                foreach (SyntaxNode node in argument.Expression.DescendantNodesAndSelf())
                {
                    if (!(node is SimpleNameSyntax name) || name.Identifier.ValueText != "NonPublic") continue;
                    if (model.GetSymbolInfo(name, ct).Symbol is IFieldSymbol flag && SymbolNames.IsNamed(flag.ContainingType, "System.Reflection.BindingFlags")) return true;
                }
            }
            return false;
        }

        // FieldInfo.SetValue or GetValue, except on the result of a GetField that is reported itself.
        private static bool IsFieldValueAccess(InvocationExpressionSyntax invocation, IMethodSymbol method, SemanticModel model, System.Threading.CancellationToken ct)
        {
            if (method.Name != "SetValue" && method.Name != "GetValue") return false;
            if (!SymbolNames.IsOrDerivesFrom(method.ContainingType, "System.Reflection.FieldInfo")) return false;
            if (!(invocation.Expression is MemberAccessExpressionSyntax access) || !(access.Expression is InvocationExpressionSyntax lookup)) return true;
            return !(model.GetSymbolInfo(lookup, ct).Symbol is IMethodSymbol lookupMethod) || !IsPrivateFieldLookup(lookup, lookupMethod, model, ct);
        }

        private static bool IsInFakes(ISymbol enclosing)
        {
            for (INamedTypeSymbol type = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; type != null; type = type.ContainingType)
            {
                if (type.Name.EndsWith("Fakes", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
