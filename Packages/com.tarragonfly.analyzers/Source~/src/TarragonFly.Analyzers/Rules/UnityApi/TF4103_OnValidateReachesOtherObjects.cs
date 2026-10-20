using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.UnityApi
{
    // TF4103, TF4104, TF4105: OnValidate that does more than check this object's own values.
    //
    // Unity runs OnValidate often, while loading and on a loading thread, and the other objects might
    // not have had Awake yet. The project rule is that OnValidate clamps and normalizes this object's
    // own values and nothing more. Three rules, since the fixes differ:
    //
    // TF4103, as in "void OnValidate() { _renderer = GetComponent<SpriteRenderer>(); }": a reference
    // found there depends on what happens to be loaded, and a field set there hides a missing
    // Inspector assignment until a build. Counted: setting a field or property of this object whose
    // type is a Unity object, including to null; GetComponent and its relatives, Transform.Find and
    // GetChild, scene searches, Resources and AssetDatabase loads.
    //
    // TF4104, as in "void OnValidate() { _label.text = "v" + Application.version; }": a change to
    // another object is a change nobody made in the Inspector, on an object that might not be set up yet.
    // Counted: an assignment to a member of another Unity object, and a void method called on one,
    // such as "transform.position = ..." or "gameObject.SetActive(false)".
    //
    // TF4105, as in "void OnValidate() { EditorUtility.SetDirty(this); }": Instantiate, Destroy,
    // DestroyImmediate, AddComponent, CreateInstance and new GameObject; SetDirty, Undo, prefab and
    // asset saves and scene dirtying. That's a job for an editor command you run on purpose.
    //
    // All three look at OnValidate on any Unity object and at the same-file private helpers it calls
    // or hands to a callback such as EditorApplication.delayCall. Lambdas in OnValidate count too,
    // since deferred work is still OnValidate's work.
    // Values that are not Unity objects, such as ints, strings and [Serializable] classes, are this
    // object's own. Reported on the assignment or the call. Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OnValidateReachesOtherObjectsAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor FindsOrAssignsReference = Descriptors.Create(
            "TF4103",
            "OnValidate finds or assigns a reference",
            "OnValidate {0}; assign references in the Inspector or look them up in Awake",
            "Unity",
            CodeKind.Game);

        public static readonly DiagnosticDescriptor ChangesAnotherObject = Descriptors.Create(
            "TF4104",
            "OnValidate changes another Unity object",
            "OnValidate {0}; change other objects from an editor command or at runtime",
            "Unity",
            CodeKind.Game);

        public static readonly DiagnosticDescriptor CreatesDestroysOrSaves = Descriptors.Create(
            "TF4105",
            "OnValidate creates, destroys or saves objects",
            "OnValidate {0}; do that from an editor command",
            "Unity",
            CodeKind.Game);

        private const string MessageName = "OnValidate";

        private static readonly HashSet<string> ComponentLookups = new HashSet<string>
        {
            "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren", "GetComponentInParent", "GetComponentsInParent",
            "TryGetComponent", "GetComponentAtIndex"
        };

        // Methods that find or load objects, by declaring type.
        private static readonly Dictionary<string, HashSet<string>> FindingMethods = new Dictionary<string, HashSet<string>>
        {
            ["UnityEngine.Component"] = ComponentLookups,
            ["UnityEngine.Object"] = new HashSet<string> { "FindObjectOfType", "FindFirstObjectByType", "FindAnyObjectByType", "FindObjectsOfType", "FindObjectsByType" },
            ["UnityEngine.GameObject"] = new HashSet<string>(ComponentLookups) { "Find", "FindWithTag", "FindGameObjectWithTag", "FindGameObjectsWithTag" },
            ["UnityEngine.Transform"] = new HashSet<string> { "Find", "GetChild" },
            ["UnityEngine.Resources"] = new HashSet<string> { "Load", "LoadAll", "LoadAsync", "FindObjectsOfTypeAll" },
            ["UnityEditor.AssetDatabase"] = new HashSet<string> { "LoadAssetAtPath", "LoadAllAssetsAtPath", "LoadAllAssetRepresentationsAtPath", "LoadMainAssetAtPath", "FindAssets" }
        };

        // Methods that make, destroy or save objects, by declaring type.
        private static readonly Dictionary<string, HashSet<string>> EditorWorkMethods = new Dictionary<string, HashSet<string>>
        {
            ["UnityEngine.Object"] = new HashSet<string> { "Instantiate", "Destroy", "DestroyImmediate" },
            ["UnityEngine.GameObject"] = new HashSet<string> { "AddComponent" },
            ["UnityEngine.ScriptableObject"] = new HashSet<string> { "CreateInstance" },
            ["UnityEditor.AssetDatabase"] = new HashSet<string>
            {
                "SaveAssets", "SaveAssetIfDirty", "CreateAsset", "AddObjectToAsset", "ImportAsset", "Refresh",
                "DeleteAsset", "DeleteAssets", "MoveAsset", "CopyAsset", "RenameAsset"
            },
            ["UnityEditor.EditorUtility"] = new HashSet<string> { "SetDirty" },
            ["UnityEditor.Undo"] = new HashSet<string> { "RecordObject", "RecordObjects", "RegisterCompleteObjectUndo", "RegisterCreatedObjectUndo", "DestroyObjectImmediate" },
            ["UnityEditor.PrefabUtility"] = new HashSet<string> { "RecordPrefabInstancePropertyModifications", "SaveAsPrefabAsset", "SaveAsPrefabAssetAndConnect", "ApplyPrefabInstance", "ApplyPropertyOverride" },
            ["UnityEditor.SceneManagement.EditorSceneManager"] = new HashSet<string> { "MarkSceneDirty", "MarkAllScenesDirty", "SaveScene", "SaveScenes", "SaveOpenScenes" }
        };

        private static readonly HashSet<string> CreatingOrDestroying = new HashSet<string>
        {
            "Instantiate", "Destroy", "DestroyImmediate", "AddComponent", "CreateInstance", "DeleteAsset", "DeleteAssets", "DestroyObjectImmediate"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(FindsOrAssignsReference, ChangesAnotherObject, CreatesDestroysOrSaves);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
                if (method.Identifier.ValueText != MessageName || method.ParameterList.Parameters.Count > 0) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(method, context.CancellationToken) is IMethodSymbol symbol)) return;
                if (symbol.IsStatic || !Frameworks.Unity.IsUnityObject(symbol.ContainingType)) return;

                OnValidateCheck check = new OnValidateCheck(context, symbol);
                check.Check(method, helper: null);
            }), SyntaxKind.MethodDeclaration);
        }

        // One OnValidate and the helpers it reaches, each checked once.
        private sealed class OnValidateCheck
        {
            private readonly SyntaxNodeAnalysisContext context;
            private readonly INamedTypeSymbol owner;
            private readonly HashSet<IMethodSymbol> visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            private readonly HashSet<SyntaxNode> reported = new HashSet<SyntaxNode>();

            public OnValidateCheck(SyntaxNodeAnalysisContext context, IMethodSymbol onValidate)
            {
                this.context = context;
                owner = onValidate.ContainingType;
                visited.Add(onValidate);
            }

            public void Check(MethodDeclarationSyntax method, string helper)
            {
                SyntaxNode body = (SyntaxNode)method.Body ?? method.ExpressionBody;
                if (body == null) return;

                foreach (AssignmentExpressionSyntax assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    CheckAssignment(assignment, helper);
                }
                foreach (InvocationExpressionSyntax invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (reported.Contains(invocation)) continue;
                    if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called) CheckInvocation(invocation, called, helper);
                }
                // Every method name, called or handed over as a callback such as "delayCall += Save".
                foreach (SimpleNameSyntax name in body.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    if (context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol is IMethodSymbol named) FollowHelper(named);
                }
                // BaseObjectCreationExpressionSyntax covers "new GameObject()" and the target-typed "new()".
                foreach (BaseObjectCreationExpressionSyntax creation in body.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                {
                    if (!SymbolNames.IsNamed(context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type, "UnityEngine.GameObject")) continue;
                    Report(CreatesDestroysOrSaves, creation, "creates a GameObject", helper);
                }
            }

            // A non-virtual method of the same type in the same file is OnValidate's own work, whether
            // OnValidate calls it or hands it to a callback.
            private void FollowHelper(IMethodSymbol called)
            {
                if (called.IsVirtual || called.IsAbstract || called.IsOverride) return;
                if (!SymbolEqualityComparer.Default.Equals(called.ContainingType, owner) || !visited.Add(called)) return;
                foreach (SyntaxReference reference in called.DeclaringSyntaxReferences)
                {
                    if (reference.SyntaxTree != context.Node.SyntaxTree) continue;
                    if (reference.GetSyntax(context.CancellationToken) is MethodDeclarationSyntax declaration) Check(declaration, called.Name);
                }
            }

            private void CheckAssignment(AssignmentExpressionSyntax assignment, string helper)
            {
                ISymbol target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
                if (!(target is IFieldSymbol) && !(target is IPropertySymbol)) return;

                ExpressionSyntax receiver = (assignment.Left as MemberAccessExpressionSyntax)?.Expression;
                if (IsAnotherUnityObject(receiver))
                {
                    Report(ChangesAnotherObject, assignment, $"changes '{assignment.Left}'", helper);
                    return;
                }

                ITypeSymbol valueType = target is IFieldSymbol field ? field.Type : ((IPropertySymbol)target).Type;
                if (!Frameworks.Unity.IsUnityObject(valueType)) return;
                string lookup = FindingCallIn(assignment.Right);
                Report(FindsOrAssignsReference, assignment, lookup == null ? $"assigns the reference '{assignment.Left}'" : $"sets '{assignment.Left}' from '{lookup}'", helper);
            }

            // The name of the outermost finding call in the assigned value, or null. The assignment's
            // message covers every finding call in the value, so none of them is reported on its own.
            private string FindingCallIn(ExpressionSyntax value)
            {
                string outermost = null;
                foreach (InvocationExpressionSyntax invocation in value.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
                {
                    if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol called)) continue;
                    if (!IsListed(FindingMethods, called)) continue;
                    reported.Add(invocation);
                    outermost ??= called.Name;
                }
                return outermost;
            }

            private void CheckInvocation(InvocationExpressionSyntax invocation, IMethodSymbol called, string helper)
            {
                if (IsListed(FindingMethods, called))
                {
                    string verb = ComponentLookups.Contains(called.Name) ? "looks up a component with" : "finds an object with";
                    Report(FindsOrAssignsReference, invocation.Expression, $"{verb} '{called.Name}'", helper);
                }
                else if (IsListed(EditorWorkMethods, called))
                {
                    string verb = CreatingOrDestroying.Contains(called.Name) ? "creates or destroys an object with" : "saves with";
                    Report(CreatesDestroysOrSaves, invocation.Expression, $"{verb} '{called.Name}'", helper);
                }
                else if (called.ReturnsVoid && !called.IsStatic && invocation.Expression is MemberAccessExpressionSyntax access && IsAnotherUnityObject(access.Expression))
                {
                    Report(ChangesAnotherObject, invocation.Expression, $"changes '{access.Expression}' with '{called.Name}'", helper);
                }
            }

            // Whether the member access is on a Unity object other than this one. No receiver, this
            // and base are this object; a [Serializable] class or struct held by this object is its own data.
            private bool IsAnotherUnityObject(ExpressionSyntax receiver)
            {
                if (receiver == null || receiver.IsKind(SyntaxKind.ThisExpression) || receiver.IsKind(SyntaxKind.BaseExpression)) return false;
                return Frameworks.Unity.IsUnityObject(context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type);
            }

            // ContainingType is the declaring type, so GetComponent called on a MonoBehaviour is
            // UnityEngine.Component's, and a project's own GetComponentCount() is not.
            private static bool IsListed(Dictionary<string, HashSet<string>> methodsByType, IMethodSymbol method)
            {
                foreach (KeyValuePair<string, HashSet<string>> type in methodsByType)
                {
                    if (SymbolNames.IsNamed(method.ContainingType, type.Key)) return type.Value.Contains(method.Name);
                }
                return false;
            }

            private void Report(DiagnosticDescriptor rule, SyntaxNode node, string what, string helper)
            {
                if (!reported.Add(node)) return;
                string message = helper == null ? what : $"{what} through '{helper}'";
                context.ReportDiagnostic(Diagnostic.Create(rule, node.GetLocation(), message));
            }
        }
    }
}