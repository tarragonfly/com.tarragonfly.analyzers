using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2048: "if (base.Equals(other)) return true;" in an Equals override whose base class has its own
    // Equals.
    //
    // The shortcut is meant as "same object", but the base class compares its own fields, so two
    // objects that differ only in the derived fields count as equal. Use ReferenceEquals(this, other).
    //
    // Only that shortcut shape is checked; combining base.Equals with the derived checks
    // ("base.Equals(other) && ...") is the normal pattern and fine. Classes extending object directly
    // are TF2046's job. Reported on the base.Equals call.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BaseEqualsAsIdentityCheckAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2048",
            "base.Equals used as a same-object check",
            "base.Equals compares '{0}' fields, not identity; use ReferenceEquals(this, other) for the shortcut",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax call = (InvocationExpressionSyntax)context.Node;
                if (!IsSameObjectShortcut(call) || !(call.Expression is MemberAccessExpressionSyntax access)) return;
                if (!(access.Expression is BaseExpressionSyntax) || access.Name.Identifier.ValueText != "Equals") return;

                MethodDeclarationSyntax method = call.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (method == null || method.Identifier.ValueText != "Equals") return;
                if (!(context.SemanticModel.GetSymbolInfo(call, context.CancellationToken).Symbol is IMethodSymbol baseEquals)) return;
                if (baseEquals.ContainingType.SpecialType == SpecialType.System_Object) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, call.GetLocation(), baseEquals.ContainingType.Name));
            }), SyntaxKind.InvocationExpression);
        }

        // "if (call) return true;", the usual opening of an Equals method.
        internal static bool IsSameObjectShortcut(InvocationExpressionSyntax call)
        {
            if (!(call.Parent is IfStatementSyntax ifStatement) || ifStatement.Condition != call) return false;
            StatementSyntax statement = ifStatement.Statement;
            if (statement is BlockSyntax block)
            {
                if (block.Statements.Count != 1) return false;
                statement = block.Statements[0];
            }
            return statement is ReturnStatementSyntax returned && returned.Expression != null && returned.Expression.IsKind(SyntaxKind.TrueLiteralExpression);
        }
    }
}