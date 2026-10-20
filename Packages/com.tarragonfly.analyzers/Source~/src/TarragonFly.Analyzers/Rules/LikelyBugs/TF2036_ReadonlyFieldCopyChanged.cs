using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2036: a property or field changed through a readonly field whose type is a type parameter that
    // may be a struct, as in "stats.Health = 5" or "stats.Health++" with "readonly T stats".
    //
    // When T is a struct, reading a readonly field makes a copy, so the change goes to the copy and the
    // stored value never changes.
    //
    // Any assignment counts, compound ones and ??= included, as do ++, --, tuple assignments and
    // subscribing to an event through the field. Type parameters constrained to class, or to a class
    // type such as ScriptableObject, are fine. So are changes to the object's own field made directly
    // in a constructor of its type (an instance constructor for an instance field, the static one for a
    // static field) or in an init accessor, where the field is still writable. Another object's field
    // still counts there, and so do lambdas and local functions, which run later. Concrete struct types
    // aren't checked, since the C# compiler already rejects that (CS1648). Reported on the changed
    // member.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ReadonlyFieldCopyChangedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2036",
            "Change made to a copy of a readonly field",
            "If '{0}' is a struct this changes a copy and the field keeps its old value; drop readonly or constrain the type to class",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                foreach (ExpressionSyntax target in Targets(context.Node))
                {
                    if (!(target is MemberAccessExpressionSyntax member)) continue;
                    if (!(context.SemanticModel.GetSymbolInfo(member.Expression, context.CancellationToken).Symbol is IFieldSymbol field) || !field.IsReadOnly) continue;
                    if (!(field.Type is ITypeParameterSymbol parameter) || parameter.IsReferenceType) continue;
                    if (IsOwnField(member.Expression, field) && IsFieldWritableAt(context.SemanticModel, member, field, context.CancellationToken)) continue;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, member.GetLocation(), field.Name));
                }
            }), SyntaxKind.SimpleAssignmentExpression, SyntaxKind.AddAssignmentExpression, SyntaxKind.SubtractAssignmentExpression,
                SyntaxKind.MultiplyAssignmentExpression, SyntaxKind.DivideAssignmentExpression, SyntaxKind.ModuloAssignmentExpression,
                SyntaxKind.AndAssignmentExpression, SyntaxKind.OrAssignmentExpression, SyntaxKind.ExclusiveOrAssignmentExpression,
                SyntaxKind.LeftShiftAssignmentExpression, SyntaxKind.RightShiftAssignmentExpression, SyntaxKind.CoalesceAssignmentExpression,
                SyntaxKind.PreIncrementExpression, SyntaxKind.PreDecrementExpression, SyntaxKind.PostIncrementExpression, SyntaxKind.PostDecrementExpression);
        }

        // What the assignment, ++ or -- changes; each element of a tuple assignment.
        private static IEnumerable<ExpressionSyntax> Targets(SyntaxNode node)
        {
            switch (node)
            {
                case AssignmentExpressionSyntax assignment when assignment.Left is TupleExpressionSyntax tuple:
                    return Elements(tuple);
                case AssignmentExpressionSyntax assignment:
                    return new[] { assignment.Left };
                case PrefixUnaryExpressionSyntax prefix:
                    return new[] { prefix.Operand };
                case PostfixUnaryExpressionSyntax postfix:
                    return new[] { postfix.Operand };
                default:
                    return Array.Empty<ExpressionSyntax>();
            }
        }

        private static IEnumerable<ExpressionSyntax> Elements(TupleExpressionSyntax tuple)
        {
            foreach (ArgumentSyntax argument in tuple.Arguments)
            {
                if (argument.Expression is TupleExpressionSyntax nested)
                {
                    foreach (ExpressionSyntax element in Elements(nested)) yield return element;
                }
                else
                {
                    yield return argument.Expression;
                }
            }
        }

        // "stats", "this.stats" or a static field: the object's own field rather than another object's.
        private static bool IsOwnField(ExpressionSyntax access, IFieldSymbol field)
        {
            if (access is IdentifierNameSyntax) return true;
            return access is MemberAccessExpressionSyntax member && (field.IsStatic || member.Expression is ThisExpressionSyntax);
        }

        // Directly in a constructor of the field's type, or in an init accessor for an instance field,
        // the field is still writable, so the change reaches it. Lambdas and local functions run later.
        private static bool IsFieldWritableAt(SemanticModel model, SyntaxNode node, IFieldSymbol field, System.Threading.CancellationToken ct)
        {
            foreach (SyntaxNode ancestor in node.Ancestors())
            {
                switch (ancestor)
                {
                    case AnonymousFunctionExpressionSyntax _:
                    case LocalFunctionStatementSyntax _:
                        return false;
                    case ConstructorDeclarationSyntax constructor:
                        return constructor.Modifiers.Any(SyntaxKind.StaticKeyword) == field.IsStatic && IsDeclaredIn(model, constructor, field, ct);
                    case AccessorDeclarationSyntax accessor when accessor.IsKind(SyntaxKind.InitAccessorDeclaration):
                        return !field.IsStatic && IsDeclaredIn(model, accessor, field, ct);
                    case MemberDeclarationSyntax _:
                        return false;
                }
            }
            return false;
        }

        private static bool IsDeclaredIn(SemanticModel model, SyntaxNode member, IFieldSymbol field, System.Threading.CancellationToken ct)
        {
            return model.GetDeclaredSymbol(member, ct) is IMethodSymbol method && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, field.ContainingType.OriginalDefinition);
        }
    }
}