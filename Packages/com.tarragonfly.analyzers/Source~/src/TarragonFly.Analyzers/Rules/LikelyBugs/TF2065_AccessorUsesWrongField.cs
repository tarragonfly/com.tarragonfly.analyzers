using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2065: a property getter that returns, or setter that assigns, a different field than the one
    // named after the property, as in "Health { get { return armor; } }" next to a "health" field.
    //
    // A copy-pasted accessor is easy to miss in review and makes the Inspector or UI show the wrong
    // stat.
    //
    // Only simple accessors are checked: a getter that just returns a field, or something read from
    // one, as in "=> items.ToArray()", and a setter that just assigns value to a field or to something
    // inside one, as in "data.flag = value". The field named after the property is found ignoring case
    // and a leading "_" or "m_", and must have the property's type and be static exactly when the
    // property is, so a read-only wrapper such as "IReadOnlyList<Card> Cards => readOnlyCards" next to
    // a "List<Card> cards" field is fine. Reported on the field the accessor uses, or on the get
    // keyword when a getter reads something inside that field.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class AccessorUsesWrongFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2065",
            "Accessor uses a different field than expected",
            "'{0}' uses '{1}', but the field for it looks like '{2}'",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                PropertyDeclarationSyntax property = (PropertyDeclarationSyntax)context.Node;
                if (!(context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is IPropertySymbol symbol)) return;
                IFieldSymbol expected = FieldNamedLike(symbol);
                if (expected == null) return;

                if (property.ExpressionBody != null)
                {
                    ReportWrongField(context, ReadFrom(property.ExpressionBody.Expression), symbol, expected, null);
                    return;
                }
                if (property.AccessorList == null) return;
                foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors)
                {
                    ExpressionSyntax body = OnlyExpression(accessor);
                    if (accessor.IsKind(SyntaxKind.GetAccessorDeclaration))
                    {
                        ExpressionSyntax field = ReadFrom(body);
                        ReportWrongField(context, field, symbol, expected, field == body ? null : accessor.Keyword.GetLocation());
                    }
                    else if (body is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                        && assignment.Right is IdentifierNameSyntax value && value.Identifier.ValueText == "value")
                    {
                        ReportWrongField(context, ReadFrom(assignment.Left), symbol, expected, null);
                    }
                }
            }), SyntaxKind.PropertyDeclaration);
        }

        // Reported at the given location, or on the field's name when there's none.
        private static void ReportWrongField(SyntaxNodeAnalysisContext context, ExpressionSyntax expression, IPropertySymbol property, IFieldSymbol expected, Location location)
        {
            SimpleNameSyntax name = expression is MemberAccessExpressionSyntax access && access.Expression is ThisExpressionSyntax
                ? access.Name
                : expression as IdentifierNameSyntax;
            if (name == null) return;
            if (!(context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is IFieldSymbol used) || SymbolEqualityComparer.Default.Equals(used, expected)) return;
            if (!SymbolEqualityComparer.Default.Equals(used.ContainingType, property.ContainingType)) return;
            context.ReportDiagnostic(Diagnostic.Create(Rule, location ?? name.GetLocation(), property.Name, used.Name, expected.Name));
        }

        // The start of a chain such as "items.ToArray()" or "stats[0].Value", where the value is read.
        private static ExpressionSyntax ReadFrom(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case InvocationExpressionSyntax invocation:
                        expression = invocation.Expression;
                        break;
                    case ElementAccessExpressionSyntax element:
                        expression = element.Expression;
                        break;
                    case MemberAccessExpressionSyntax access when !(access.Expression is ThisExpressionSyntax):
                        expression = access.Expression;
                        break;
                    default:
                        return expression;
                }
            }
        }

        private static IFieldSymbol FieldNamedLike(IPropertySymbol property)
        {
            foreach (ISymbol member in property.ContainingType.GetMembers())
            {
                if (!(member is IFieldSymbol field) || field.IsImplicitlyDeclared || field.IsStatic != property.IsStatic) continue;
                if (!SymbolEqualityComparer.Default.Equals(field.Type, property.Type)) continue;
                if (string.Equals(WithoutPrefix(field.Name), property.Name, StringComparison.OrdinalIgnoreCase)) return field;
            }
            return null;
        }

        private static string WithoutPrefix(string name)
        {
            if (name.StartsWith("m_", StringComparison.Ordinal)) return name.Substring(2);
            return name.TrimStart('_');
        }

        private static ExpressionSyntax OnlyExpression(AccessorDeclarationSyntax accessor)
        {
            if (accessor.ExpressionBody != null) return accessor.ExpressionBody.Expression;
            if (accessor.Body == null || accessor.Body.Statements.Count != 1) return null;
            switch (accessor.Body.Statements[0])
            {
                case ReturnStatementSyntax statement: return statement.Expression;
                case ExpressionStatementSyntax statement: return statement.Expression;
                default: return null;
            }
        }
    }
}