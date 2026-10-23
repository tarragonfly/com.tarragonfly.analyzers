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
    // type is a Unity object or an array or list of them, including to null; setting an element of,
    // adding to, removing from or clearing this object's array or list of Unity objects;
    // GetComponent and its relatives, Transform.Find and GetChild, scene searches, Resources and
    // AssetDatabase loads.
    //
    // TF4104, as in "void OnValidate() { _label.text = "v" + Application.version; }": a change to
    // another object is a change nobody made in the Inspector, on an object that might not be set up yet.
    // Counted: an assignment to a member of another Unity object, and a void method called on one,
    // such as "transform.position = ..." or "gameObject.SetActive(false)". A component's own
    // "name = ..." counts too, since that's the GameObject's name.
    //
    // TF4105, as in "void OnValidate() { EditorUtility.SetDirty(this); }": Instantiate,
    // InstantiatePrefab, Destroy, DestroyImmediate, AddComponent, CreateInstance and "new" of any
    // Unity object, such as new Material(...); SetDirty, Undo, prefab and asset saves and scene
    // dirtying; and an asset's own "name = ...", which stops matching its file name. That's a job for
    // an editor command you run on purpose.
    //
    // All three look at OnValidate on any Unity object and at the same-file private helpers and
    // property getters and setters it reaches, including helpers handed to a callback such as
    // EditorApplication.delayCall. Lambdas in OnValidate count too, since deferred work is still
    // OnValidate's work. Helpers in another file or a base class aren't followed, since a report has
    // to land in the file being checked.
    // Values that are not Unity objects, such as ints, strings and [Serializable] classes, are this
    // object's own, and so are this object's enabled and hideFlags. Reported on the assignment or
    // the call. Game code only.
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
            ["UnityEditor.Undo"] = new HashSet<string> { "RecordObject", "RecordObjects", "RegisterCompleteObjectUndo", "RegisterCreatedObjectUndo", "DestroyObjectImmediate", "AddComponent" },
            ["UnityEditor.PrefabUtility"] = new HashSet<string>
            {
                "InstantiatePrefab", "RecordPrefabInstancePropertyModifications", "SaveAsPrefabAsset", "SaveAsPrefabAssetAndConnect", "ApplyPrefabInstance", "ApplyPropertyOverride"
            },
            ["UnityEditor.SceneManagement.EditorSceneManager"] = new HashSet<string> { "MarkSceneDirty", "MarkAllScenesDirty", "SaveScene", "SaveScenes", "SaveOpenScenes" }
        };

        private static readonly HashSet<string> CreatingOrDestroying = new HashSet<string>
        {
            "Instantiate", "InstantiatePrefab", "Destroy", "DestroyImmediate", "AddComponent", "CreateInstance", "DeleteAsset", "DeleteAssets", "DestroyObjectImmediate"
        };

        // Methods that change what a list, set or dictionary holds.
        private static readonly HashSet<string> CollectionChanges = new HashSet<string>
        {
            "Add", "AddRange", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange", "Clear"
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
                check.Check(BodyOf(method), helper: null);
            }), SyntaxKind.MethodDeclaration);
        }

        // The code a method or accessor runs, or null for an auto-property accessor or an abstract method.
        // An expression-bodied property's getter is declared by its "=> ..." clause.
        private static SyntaxNode BodyOf(SyntaxNode declaration)
        {
            switch (declaration)
            {
                case BaseMethodDeclarationSyntax method: return (SyntaxNode)method.Body ?? method.ExpressionBody;
                case AccessorDeclarationSyntax accessor: return (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
                case ArrowExpressionClauseSyntax arrow: return arrow;
                default: return null;
            }
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

            public void Check(SyntaxNode body, string helper)
            {
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
                // Every method name, called or handed over as a callback such as "delayCall += Save",
                // and every property read or written. nameof(Body) runs nothing.
                foreach (SimpleNameSyntax name in body.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    ISymbol named = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
                    if (!(named is IMethodSymbol) && !(named is IPropertySymbol)) continue;
                    if (name.Ancestors().TakeWhile(node => node != body).Any(IsNameOf)) continue;
                    if (named is IMethodSymbol method) FollowHelper(method, method.Name);
                    else FollowProperty(name, (IPropertySymbol)named);
                }
                // BaseObjectCreationExpressionSyntax covers "new Material(...)" and the target-typed "new()".
                foreach (BaseObjectCreationExpressionSyntax creation in body.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                {
                    ITypeSymbol created = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
                    if (!Frameworks.Unity.IsUnityObject(created)) continue;
                    Report(CreatesDestroysOrSaves, creation, $"creates a new {created.Name}", helper);
                }
            }

            // A non-virtual method or accessor of the same type in the same file is OnValidate's own
            // work, whether OnValidate calls it or hands it to a callback.
            private void FollowHelper(IMethodSymbol called, string helper)
            {
                if (called == null || called.IsVirtual || called.IsAbstract || called.IsOverride) return;
                if (!SymbolEqualityComparer.Default.Equals(called.ContainingType, owner) || !visited.Add(called)) return;
                foreach (SyntaxReference reference in called.DeclaringSyntaxReferences)
                {
                    if (reference.SyntaxTree != context.Node.SyntaxTree) continue;
                    Check(BodyOf(reference.GetSyntax(context.CancellationToken)), helper);
                }
            }

            // A getter that caches a lookup, as in "Renderer Body => _body ??= GetComponent<Renderer>()",
            // runs when OnValidate reads the property, and the setter runs when OnValidate writes it.
            // "Size += 1" and "Size++" run both; "(Size, _) = (1, 2)" only the setter.
            private void FollowProperty(SimpleNameSyntax name, IPropertySymbol property)
            {
                SyntaxNode use = name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : (SyntaxNode)name;
                SyntaxNode target = use;
                while (target.Parent is ArgumentSyntax && target.Parent.Parent is TupleExpressionSyntax tuple) target = tuple;

                bool read = true;
                bool written = false;
                if (target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target)
                {
                    written = true;
                    read = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
                }
                else if (use.Parent.IsKind(SyntaxKind.PreIncrementExpression) || use.Parent.IsKind(SyntaxKind.PreDecrementExpression)
                    || use.Parent.IsKind(SyntaxKind.PostIncrementExpression) || use.Parent.IsKind(SyntaxKind.PostDecrementExpression))
                {
                    written = true;
                }
                if (read) FollowHelper(property.GetMethod, property.Name);
                if (written) FollowHelper(property.SetMethod, property.Name);
            }

            private void CheckAssignment(AssignmentExpressionSyntax assignment, string helper)
            {
                if (assignment.Left is ElementAccessExpressionSyntax element)
                {
                    CheckElementAssignment(assignment, element, helper);
                    return;
                }

                ISymbol target = context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol;
                if (!(target is IFieldSymbol) && !(target is IPropertySymbol)) return;

                ExpressionSyntax receiver = (assignment.Left as MemberAccessExpressionSyntax)?.Expression;
                if (IsAnotherUnityObject(receiver))
                {
                    Report(ChangesAnotherObject, assignment, $"changes '{assignment.Left}'", helper);
                    return;
                }
                if (target is IPropertySymbol { Name: "name" } && SymbolNames.IsNamed(target.ContainingType, "UnityEngine.Object"))
                {
                    if (SymbolNames.IsOrDerivesFrom(owner, "UnityEngine.Component")) Report(ChangesAnotherObject, assignment, "renames the GameObject with 'name'", helper);
                    else Report(CreatesDestroysOrSaves, assignment, "renames the asset with 'name'", helper);
                    return;
                }

                ITypeSymbol valueType = target is IFieldSymbol field ? field.Type : ((IPropertySymbol)target).Type;
                if (Frameworks.Unity.IsUnityObject(valueType)) ReportReference(assignment, "reference", helper);
                else if (IsListOfUnityObjects(valueType)) ReportReference(assignment, "reference list", helper);
            }

            // A whole array or list swapped out, as in "huds = huds.Where(...).ToArray()", drops
            // Inspector references the same way RemoveAll does.
            private static bool IsListOfUnityObjects(ITypeSymbol type)
            {
                if (type is IArrayTypeSymbol array) return Frameworks.Unity.IsUnityObject(array.ElementType);
                return type is INamedTypeSymbol named && named.TypeArguments.Any(Frameworks.Unity.IsUnityObject)
                    && named.AllInterfaces.Any(implemented => implemented.SpecialType == SpecialType.System_Collections_IEnumerable);
            }

            private void ReportReference(AssignmentExpressionSyntax assignment, string noun, string helper)
            {
                string lookup = FindingCallIn(assignment.Right);
                Report(FindsOrAssignsReference, assignment, lookup == null ? $"assigns the {noun} '{assignment.Left}'" : $"sets '{assignment.Left}' from '{lookup}'", helper);
            }

            // "targets[0] = ..." on this object's array or list of Unity objects, and any element
            // written into another Unity object's array or list, as in "catalog.prefabs[0] = null".
            // A local list is scratch space and isn't reported.
            private void CheckElementAssignment(AssignmentExpressionSyntax assignment, ElementAccessExpressionSyntax element, string helper)
            {
                if (GoesThroughAnotherUnityObject(element.Expression))
                {
                    Report(ChangesAnotherObject, assignment, $"changes '{assignment.Left}'", helper);
                    return;
                }
                if (!IsHeldByThisObject(element.Expression)) return;
                if (!Frameworks.Unity.IsUnityObject(context.SemanticModel.GetTypeInfo(element, context.CancellationToken).Type)) return;
                ReportReference(assignment, "reference", helper);
            }

            // The name of the outermost finding call in the assigned value or the list call's arguments,
            // or null. The one message covers every finding call in there, so none of them is reported
            // on its own.
            private string FindingCallIn(SyntaxNode value)
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
                else if (IsCollectionChange(invocation, called, out ExpressionSyntax collection))
                {
                    CheckCollectionChange(invocation, called, collection, helper);
                }
                else if (called.ReturnsVoid && !called.IsStatic)
                {
                    ExpressionSyntax receiver = ReceiverOf(invocation.Expression);
                    if (IsAnotherUnityObject(receiver)) Report(ChangesAnotherObject, invocation.Expression, $"changes '{TextOf(receiver)}' with '{called.Name}'", helper);
                }
            }

            // "targets.Add(...)", "targets?.Clear()" and the like, through the collection's own methods
            // such as List<T>.RemoveAll. A Unity object's own Clear() is a void call on that object.
            private bool IsCollectionChange(InvocationExpressionSyntax invocation, IMethodSymbol called, out ExpressionSyntax collection)
            {
                collection = ReceiverOf(invocation.Expression);
                if (collection == null || called.IsStatic || called.IsExtensionMethod || !CollectionChanges.Contains(called.Name)) return false;
                bool isCollection = called.ContainingType.AllInterfaces.Any(type => type.SpecialType == SpecialType.System_Collections_IEnumerable);
                return isCollection && !IsAnotherUnityObject(collection);
            }

            // Another Unity object's collection of anything, or this object's own collection of Unity objects.
            private void CheckCollectionChange(InvocationExpressionSyntax invocation, IMethodSymbol called, ExpressionSyntax collection, string helper)
            {
                if (GoesThroughAnotherUnityObject(collection))
                {
                    Report(ChangesAnotherObject, invocation.Expression, $"changes '{TextOf(collection)}' with '{called.Name}'", helper);
                    return;
                }
                if (!called.ContainingType.TypeArguments.Any(Frameworks.Unity.IsUnityObject) || !IsHeldByThisObject(collection)) return;
                string lookup = FindingCallIn(invocation.ArgumentList);
                string what = $"changes the reference list '{TextOf(collection)}' with '{called.Name}'";
                Report(FindsOrAssignsReference, invocation.Expression, lookup == null ? what : $"{what} from '{lookup}'", helper);
            }

            // A field or property of this object, or of a [Serializable] value it holds, rather than a
            // local or a member of another Unity object.
            private bool IsHeldByThisObject(ExpressionSyntax expression)
            {
                ISymbol symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
                return (symbol is IFieldSymbol || symbol is IPropertySymbol) && !GoesThroughAnotherUnityObject(expression);
            }

            // Whether a member chain such as "catalog.data.prefabs" or "catalog?.prefabs" reaches into
            // a Unity object other than this one at any step.
            private bool GoesThroughAnotherUnityObject(ExpressionSyntax expression)
            {
                for (ExpressionSyntax receiver = ReceiverOf(expression); receiver != null; receiver = ReceiverOf(receiver))
                {
                    if (IsAnotherUnityObject(receiver)) return true;
                }
                return false;
            }

            // Puts the receiver back in front of a ".prefabs" after "?.", so the message says "catalog?.prefabs".
            private static string TextOf(ExpressionSyntax expression)
            {
                ExpressionSyntax start = expression;
                while (start is MemberAccessExpressionSyntax access) start = access.Expression;
                ExpressionSyntax receiver = start is MemberBindingExpressionSyntax ? ReceiverOf(start) : null;
                return receiver == null ? expression.ToString() : $"{TextOf(receiver)}?{expression}";
            }

            // "list" in both "list.Clear" and "list?.Clear", or null for a plain "Clear". A "?." binding
            // belongs to the nearest "?." whose right-hand part holds it, as in "a?.b?.Clear()".
            private static ExpressionSyntax ReceiverOf(ExpressionSyntax called)
            {
                if (called is MemberAccessExpressionSyntax access) return access.Expression;
                if (!(called is MemberBindingExpressionSyntax)) return null;
                for (SyntaxNode node = called; node.Parent != null; node = node.Parent)
                {
                    if (node.Parent is ConditionalAccessExpressionSyntax conditional && conditional.WhenNotNull == node) return conditional.Expression;
                }
                return null;
            }

            private static bool IsNameOf(SyntaxNode node)
            {
                return node is InvocationExpressionSyntax invocation && invocation.Expression is IdentifierNameSyntax name && name.Identifier.ValueText == "nameof";
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