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
    // the body included. A field reached through a variable declared in the loop, as in "group.Key =
    // prefix + group.Key" for each group, or through the element the loop's variable indexes, as in
    // "groups[i].Key += suffix" for each i, is a different string on every pass and isn't counted.
    // In "s = s + x" both sides have to name s the same way: "first.Key = second.Key + x" copies
    // from another object, and "this.text = text + x" isn't matched either. Interpolated strings and
    // string.Concat aren't covered. Reported on the assignment. Game code only.
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
                if (IsDeclaredInLoop(target, loop) || OwnerChangesEveryPass(context, assignment.Left, loop)) return;

                if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && !AddsTo(context, assignment.Right, assignment.Left)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation(), target.Name));
            }), SyntaxKind.AddAssignmentExpression, SyntaxKind.SimpleAssignmentExpression);
        }

        // Whether the string belongs to an object that changes with the loop: a field of a variable
        // declared in the loop ("group.Key"), of something reached through it ("row.Parts[0].Key"), or
        // of the element the loop's own variable indexes ("groups[i].Key"). The index has to be that
        // variable itself: "groups[i + 1]" and "groups[i % 2]" are reported, since arithmetic on it can
        // come back to the same element, and so is an index held in a local declared in the body.
        private static bool OwnerChangesEveryPass(SyntaxNodeAnalysisContext context, ExpressionSyntax target, StatementSyntax loop)
        {
            ExpressionSyntax owner = target;
            while (true)
            {
                if (owner is MemberAccessExpressionSyntax access && !(access.Expression is ThisExpressionSyntax))
                {
                    owner = access.Expression;
                }
                else if (owner is ElementAccessExpressionSyntax element)
                {
                    if (element.ArgumentList.Arguments.Any(index => IsLoopVariable(context.SemanticModel.GetSymbolInfo(index.Expression, context.CancellationToken).Symbol, loop))) return true;
                    owner = element.Expression;
                }
                else
                {
                    return owner != target && IsDeclaredInLoop(context.SemanticModel.GetSymbolInfo(owner, context.CancellationToken).Symbol, loop);
                }
            }
        }

        // A local, or a parameter of a lambda or local function, declared inside the loop.
        private static bool IsDeclaredInLoop(ISymbol variable, StatementSyntax loop)
        {
            return (variable is ILocalSymbol || variable is IParameterSymbol) && variable.DeclaringSyntaxReferences.Any(reference => loop.Span.Contains(reference.Span));
        }

        // The variable a loop steps through, as in "for (int i = 0; ...)" or "foreach (string key in
        // keys)": a local declared in the loop, outside its body.
        private static bool IsLoopVariable(ISymbol variable, StatementSyntax loop)
        {
            return variable is ILocalSymbol && IsDeclaredInLoop(variable, loop)
                && !variable.DeclaringSyntaxReferences.Any(reference => BodyOf(loop).Span.Contains(reference.Span));
        }

        // The closest loop whose body holds the assignment, or null when the closest loop holds it
        // in its header instead.
        private static StatementSyntax InnermostLoop(SyntaxNode node)
        {
            for (SyntaxNode child = node, parent = node.Parent; parent != null; child = parent, parent = parent.Parent)
            {
                if (parent is MemberDeclarationSyntax) return null;
                StatementSyntax body = BodyOf(parent);
                if (body != null) return child == body ? (StatementSyntax)parent : null;
            }
            return null;
        }

        // The body of a for, foreach, while or do loop, or null for any other node.
        private static StatementSyntax BodyOf(SyntaxNode loop)
        {
            return loop is ForStatementSyntax forLoop ? forLoop.Statement
                : loop is CommonForEachStatementSyntax forEach ? forEach.Statement
                : loop is WhileStatementSyntax whileLoop ? whileLoop.Statement
                : loop is DoStatementSyntax doLoop ? doLoop.Statement : null;
        }

        // "a + b + target" with the assigned expression, written the same way, among the strings added.
        private static bool AddsTo(SyntaxNodeAnalysisContext context, ExpressionSyntax value, ExpressionSyntax target)
        {
            if (!value.IsKind(SyntaxKind.AddExpression)) return false;
            if (context.SemanticModel.GetTypeInfo(value, context.CancellationToken).Type?.SpecialType != SpecialType.System_String) return false;
            ExpressionSyntax current = value;
            while (current is BinaryExpressionSyntax add && add.IsKind(SyntaxKind.AddExpression))
            {
                if (SyntaxFactory.AreEquivalent(add.Right, target)) return true;
                current = add.Left;
            }
            return SyntaxFactory.AreEquivalent(current, target);
        }
    }
}