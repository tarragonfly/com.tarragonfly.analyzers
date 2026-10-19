using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2019: the result of a call that changes nothing is thrown away, as in "playerName.Trim();" or
    // "enemies.Where(e => e.IsAlive);".
    //
    // Strings and LINQ queries never change in place, so the call does nothing; the result needs
    // assigning ("name = name.Trim();").
    //
    // Calls that change nothing: string methods, methods on numbers, bool and char (int.Parse,
    // ToString, CompareTo), LINQ's Enumerable methods, object's own methods such as ToString and
    // GetHashCode (overrides may do more, so they aren't counted), methods marked [Pure], System.Math,
    // Unity's Mathf and the static methods of Vector2, Vector3, Vector4, Quaternion and Color. Methods
    // with out or ref parameters, such as int.TryParse, are fine. Instance methods such as
    // Vector3.Normalize() change the value and aren't checked. Reported on the call. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class IgnoredResultAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2019",
            "Result of a call without side effects is ignored",
            "'{0}' doesn't change anything; use its result or remove the call",
            "LikelyBugs",
            CodeKind.Game);

        private static readonly HashSet<string> UnityValueTypes = new HashSet<string>
        {
            "UnityEngine.Vector2", "UnityEngine.Vector3", "UnityEngine.Vector4", "UnityEngine.Quaternion", "UnityEngine.Color"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                if (!(((ExpressionStatementSyntax)context.Node).Expression is InvocationExpressionSyntax invocation)) return;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || method.ReturnsVoid) return;
                foreach (IParameterSymbol parameter in method.Parameters)
                {
                    if (parameter.RefKind != RefKind.None) return;
                }
                if (ChangesNothing(method.ReducedFrom ?? method)) context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.Name));
            }), SyntaxKind.ExpressionStatement);
        }

        private static bool ChangesNothing(IMethodSymbol method)
        {
            INamedTypeSymbol type = method.ContainingType;
            if (type == null) return false;
            if (IsValueLike(type.SpecialType)) return true;
            if (type.SpecialType == SpecialType.System_Object) return true;
            if (SymbolNames.IsNamed(type, "System.Linq.Enumerable") || SymbolNames.IsNamed(type, "System.Math") || SymbolNames.IsNamed(type, "UnityEngine.Mathf")) return true;
            if (method.IsStatic && UnityValueTypes.Contains(type.ToDisplayString())) return true;
            return SymbolNames.HasAttribute(method, "System.Diagnostics.Contracts.PureAttribute");
        }

        private static bool IsValueLike(SpecialType type)
        {
            switch (type)
            {
                case SpecialType.System_String:
                case SpecialType.System_Boolean:
                case SpecialType.System_Char:
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }
    }
}