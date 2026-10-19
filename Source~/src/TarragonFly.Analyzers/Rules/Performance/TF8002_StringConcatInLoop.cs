using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Performance
{
    // TF8002: a string built up with + inside a loop, as in "foreach (string line in lines) text +=
    // line;".
    //
    // Every pass allocates a new string holding everything so far, so the work and the garbage grow
    // with the square of the length. Use a StringBuilder, or string.Join.
    //
    // Checked: "s += x" and "s = s + x" (s anywhere in the +) on a string field, parameter or local
    // declared outside the innermost loop, in the body of a for, foreach, while or do loop, lambdas in
    // the body included. Interpolated strings and string.Concat aren't checked. Reported on the
    // assignment. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StringConcatInLoopAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF8002",
            "String built with + in a loop",
            "Build '{0}' with a StringBuilder; + in a loop allocates a new string on every pass",
            "Performance",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                StatementSyntax loop = InnermostLoop(assignment);
                if (loop == null) return;

                ISymbol target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
                if (!(target is ILocalSymbol || target is IParameterSymbol || target is IFieldSymbol)) return;
                if (context.SemanticModel.GetTypeInfo(assignment.Left, context.CancellationToken).Type?.SpecialType != SpecialType.System_String) return;
                if (target is ILocalSymbol local && local.DeclaringSyntaxReferences.Any(reference => loop.Span.Contains(reference.Span))) return;

                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && !AddsTo(context, assignment.Right, target)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation(), target.Name));
            }), SyntaxKind.AddAssignmentExpression, SyntaxKind.SimpleAssignmentExpression);
        }

        // The closest loop whose body holds the assignment, or null when the closest loop holds it
        // in its header instead.
        private static StatementSyntax InnermostLoop(SyntaxNode node)
        {
            for (SyntaxNode child = node, parent = node.Parent; parent != null; child = parent, parent = parent.Parent)
            {
                if (parent is MemberDeclarationSyntax) return null;
                StatementSyntax body = parent is ForStatementSyntax forLoop ? forLoop.Statement
                    : parent is CommonForEachStatementSyntax forEach ? forEach.Statement
                    : parent is WhileStatementSyntax whileLoop ? whileLoop.Statement
                    : parent is DoStatementSyntax doLoop ? doLoop.Statement : null;
                if (body != null) return child == body ? (StatementSyntax)parent : null;
            }
            return null;
        }

        // "a + b + target" with target among the strings added.
        private static bool AddsTo(SyntaxNodeAnalysisContext context, ExpressionSyntax value, ISymbol target)
        {
            if (!value.IsKind(SyntaxKind.AddExpression)) return false;
            if (context.SemanticModel.GetTypeInfo(value, context.CancellationToken).Type?.SpecialType != SpecialType.System_String) return false;
            ExpressionSyntax current = value;
            while (current is BinaryExpressionSyntax add && add.IsKind(SyntaxKind.AddExpression))
            {
                if (IsTarget(context, add.Right, target)) return true;
                current = add.Left;
            }
            return IsTarget(context, current, target);
        }

        private static bool IsTarget(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, ISymbol target)
        {
            return SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol, target);
        }
    }
}