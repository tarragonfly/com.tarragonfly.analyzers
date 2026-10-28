using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.UnityApi
{
    // TF4102: a scene search that fills in an Inspector reference when it is missing, as in
    // "if (hud == null) hud = FindFirstObjectByType<Hud>();" for "[SerializeField] Hud hud;".
    //
    // The search hides the missing reference instead of reporting it. It finds whichever object
    // happens to be loaded, which changes with scene order and additive loading, and the setup bug
    // shows up later as the wrong HUD or a slow frame on the first call. Assign the reference in the
    // Inspector, or log an error with the class prefix when it is missing.
    //
    // Counted: an assignment to a serialized field of a component (public, [SerializeField] or
    // [SerializeReference]) from a scene search, inside an if whose condition checks that field for
    // null ("== null", "is null", "!field"), and "field ??= search" or "field = field ?? search".
    // Scene searches: Object.FindObjectOfType, FindFirstObjectByType, FindAnyObjectByType,
    // FindObjectsOfType and FindObjectsByType, and GameObject.Find, FindWithTag,
    // FindGameObjectWithTag and FindGameObjectsWithTag. Reset is fine, since filling a field there
    // saves it in the editor; OnValidate is left to TF4103, which reports any search there. Reported
    // on the search call. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SceneSearchRepairsReferenceAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF4102",
            "Scene search fills in a missing Inspector reference",
            "'{0}' is set in the Inspector; log an error when it is missing instead of searching the scene with {1}",
            "Unity",
            CodeKind.Game);

        private static readonly HashSet<string> ObjectSearches = new HashSet<string>
        {
            "FindObjectOfType", "FindFirstObjectByType", "FindAnyObjectByType", "FindObjectsOfType", "FindObjectsByType"
        };

        private static readonly HashSet<string> GameObjectSearches = new HashSet<string>
        {
            "Find", "FindWithTag", "FindGameObjectWithTag", "FindGameObjectsWithTag"
        };

        private static readonly HashSet<string> EditorFillMessages = new HashSet<string> { "Reset", "OnValidate" };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                IfStatementSyntax ifStatement = (IfStatementSyntax)context.Node;
                if (IsEditorFill(ifStatement)) return;
                List<IFieldSymbol> checkedFields = NullCheckedFields(context.SemanticModel, ifStatement.Condition, context.CancellationToken);
                if (checkedFields.Count == 0) return;

                foreach (AssignmentExpressionSyntax assignment in ifStatement.Statement.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>())
                {
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) continue;
                    IFieldSymbol field = InspectorField(context.SemanticModel, assignment.Left, context.CancellationToken);
                    if (field == null || !checkedFields.Contains(field, SymbolEqualityComparer.Default)) continue;
                    ReportSearches(context, field, assignment.Right);
                }
            }), SyntaxKind.IfStatement);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                AssignmentExpressionSyntax assignment = (AssignmentExpressionSyntax)context.Node;
                if (IsEditorFill(assignment)) return;
                IFieldSymbol field = InspectorField(context.SemanticModel, assignment.Left, context.CancellationToken);
                if (field == null) return;
                if (assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression))
                {
                    ReportSearches(context, field, assignment.Right);
                }
                else if (assignment.Right is BinaryExpressionSyntax coalesce && coalesce.IsKind(SyntaxKind.CoalesceExpression)
                    && SymbolEqualityComparer.Default.Equals(context.SemanticModel.GetSymbolInfo(coalesce.Left, context.CancellationToken).Symbol, field))
                {
                    ReportSearches(context, field, coalesce.Right);
                }
            }), SyntaxKind.SimpleAssignmentExpression, SyntaxKind.CoalesceAssignmentExpression);
        }

        private static bool IsEditorFill(SyntaxNode node)
        {
            MethodDeclarationSyntax method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            return method != null && EditorFillMessages.Contains(method.Identifier.ValueText) && method.ParameterList.Parameters.Count == 0;
        }

        private static void ReportSearches(SyntaxNodeAnalysisContext context, IFieldSymbol field, ExpressionSyntax value)
        {
            foreach (InvocationExpressionSyntax invocation in value.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method) || !IsSceneSearch(method)) continue;
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Expression.GetLocation(), field.Name, method.Name));
            }
        }

        private static bool IsSceneSearch(IMethodSymbol method)
        {
            INamedTypeSymbol type = method.ContainingType;
            if (SymbolNames.IsNamed(type, "UnityEngine.Object")) return ObjectSearches.Contains(method.Name);
            if (SymbolNames.IsNamed(type, "UnityEngine.GameObject")) return GameObjectSearches.Contains(method.Name);
            return false;
        }

        // A serialized field of the component the code is in, or null.
        private static IFieldSymbol InspectorField(SemanticModel model, ExpressionSyntax target, CancellationToken ct)
        {
            if (!(model.GetSymbolInfo(target, ct).Symbol is IFieldSymbol field) || !Frameworks.Unity.IsSerializedByUnity(field)) return null;
            return SymbolNames.IsOrDerivesFrom(field.ContainingType, "UnityEngine.Component") ? field : null;
        }

        // Fields the condition checks for null or for a destroyed object.
        private static List<IFieldSymbol> NullCheckedFields(SemanticModel model, ExpressionSyntax condition, CancellationToken ct)
        {
            List<IFieldSymbol> fields = new List<IFieldSymbol>();
            foreach (SyntaxNode node in condition.DescendantNodesAndSelf())
            {
                ExpressionSyntax checkedExpression = null;
                switch (node)
                {
                    case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression):
                        if (binary.Right.IsKind(SyntaxKind.NullLiteralExpression)) checkedExpression = binary.Left;
                        else if (binary.Left.IsKind(SyntaxKind.NullLiteralExpression)) checkedExpression = binary.Right;
                        break;
                    case IsPatternExpressionSyntax pattern when pattern.Pattern is ConstantPatternSyntax constant && constant.Expression.IsKind(SyntaxKind.NullLiteralExpression):
                        checkedExpression = pattern.Expression;
                        break;
                    case PrefixUnaryExpressionSyntax not when not.IsKind(SyntaxKind.LogicalNotExpression):
                        checkedExpression = not.Operand;
                        break;
                }
                if (checkedExpression != null && model.GetSymbolInfo(checkedExpression, ct).Symbol is IFieldSymbol field) fields.Add(field);
            }
            return fields;
        }
    }
}