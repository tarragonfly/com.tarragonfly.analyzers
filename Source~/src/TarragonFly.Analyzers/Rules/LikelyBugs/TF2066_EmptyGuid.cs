using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2066: "new Guid()", "default(Guid)" or a Guid made from an all-zero string.
    //
    // These all give the empty Guid, which reads as if a fresh ID was meant: a save slot or item that
    // gets "new Guid()" shares its ID with every other one. Use Guid.NewGuid() for a new ID, or
    // Guid.Empty when empty is meant.
    //
    // A plain "default" literal counts when it becomes a Guid. Reported on the expression.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EmptyGuidAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2066",
            "Empty Guid that looks like a new one",
            "This is the empty Guid; use Guid.NewGuid() for a new ID or Guid.Empty if empty is meant",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax expression = (ExpressionSyntax)context.Node;
                TypeInfo type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
                ITypeSymbol checkedType = expression.IsKind(SyntaxKind.DefaultLiteralExpression) ? type.ConvertedType : type.Type;
                if (!SymbolNames.IsNamed(checkedType, "System.Guid")) return;

                if (expression is BaseObjectCreationExpressionSyntax creation && !IsEmpty(context, creation)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation()));
            }), SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression, SyntaxKind.DefaultExpression, SyntaxKind.DefaultLiteralExpression);
        }

        private static bool IsEmpty(SyntaxNodeAnalysisContext context, BaseObjectCreationExpressionSyntax creation)
        {
            if (creation.ArgumentList == null || creation.ArgumentList.Arguments.Count == 0) return true;
            if (creation.ArgumentList.Arguments.Count != 1) return false;
            Optional<object> value = context.SemanticModel.GetConstantValue(creation.ArgumentList.Arguments[0].Expression, context.CancellationToken);
            return value.HasValue && value.Value is string text && Guid.TryParse(text, out Guid parsed) && parsed == Guid.Empty;
        }
    }
}