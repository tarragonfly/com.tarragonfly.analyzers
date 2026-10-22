using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2511: a handler added to another object's event that this type never removes, as in
    // "UserInput.OnPause += Pause;" in OnEnable with no "UserInput.OnPause -= Pause;" anywhere in
    // the class, or "playButton.onClick.AddListener(Play)" with no RemoveListener.
    //
    // The event keeps a reference to the subscriber, so a destroyed MonoBehaviour is still called
    // (MissingReferenceException, or a menu that reacts while closed) and never collected. The
    // pair belongs in matching lifecycle methods: OnEnable with OnDisable, Awake with OnDestroy.
    //
    // Checked: += on an event or delegate-typed field or property, and AddListener on a UnityEvent,
    // when the handler is a method group or a delegate held in a field, property, local or
    // parameter. The type counts as unsubscribing when it has -= or RemoveListener for the same
    // event and handler anywhere, or RemoveAllListeners on that event; which object the event
    // belongs to is not compared. Lambdas aren't checked, since nothing can remove them and TF2505
    // and TF4100 report the attempts.
    //
    // Left alone, because the event can't outlive the subscriber or doesn't hold on to it:
    // - events the type declares itself or inherits, reached through this or as its own statics,
    //   since the type raises them;
    // - subscriptions in a static constructor, a [RuntimeInitializeOnLoadMethod] or an
    //   [InitializeOnLoadMethod], which run once per domain;
    // - events of a component's own GameObject or its children: an object reached from this,
    //   gameObject or transform through GetComponent, GetComponentInChildren, TryGetComponent,
    //   AddComponent, Transform.Find or GetChild, ScrollRect.content, array elements and foreach,
    //   directly or through locals and fields that only ever get such values. Fields Unity saves
    //   are left out, since the Inspector can point them anywhere, and so is GetComponentInParent;
    // - objects the type creates itself with new, such as an InputAction made in OnEnable, held in a
    //   local or in fields that only ever get such values (a "= null" reset doesn't count against
    //   them), since nothing else holds on to them. Only while the object stays private, though: a
    //   Unity object made with new lives in the scene, and an object passed as an argument, stored,
    //   returned or captured by a lambda can outlive the type, so those are reported. Calling its
    //   methods, extension methods too, doesn't count as passing it on;
    // - AsyncOperation.completed and Addressables' AsyncOperationHandle.Completed, which fire once
    //   when the load finishes and are dropped with the operation, and EditorApplication.delayCall,
    //   which Unity clears before calling it;
    // - UI Toolkit's Button.clicked, like RegisterCallback, which isn't covered either.
    // A subscription meant to last for the object's whole life gets a suppression with a reason.
    // Reported on the subscription. Game code only: tests create and destroy what they subscribe to
    // in SetUp and TearDown.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EventNeverUnsubscribedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2511",
            "Event subscribed but never unsubscribed",
            "'{0}' is subscribed here but never unsubscribed in this type; pair it in the matching lifecycle method",
            "Inheritance",
            CodeKind.Game,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        // Instance methods that return the object they're called on, a component on it, or a child of it.
        private static readonly string[] OwnHierarchyLookups =
        {
            "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren", "TryGetComponent", "AddComponent", "Find", "GetChild"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentBag<Subscription> subscriptions = new ConcurrentBag<Subscription>();
            // Keys are "type|event|handler"; "type|event|*" is a RemoveAllListeners on that event.
            ConcurrentDictionary<string, bool> unsubscribed = new ConcurrentDictionary<string, bool>();
            ComponentFieldValues fieldValues = new ComponentFieldValues();

            start.RegisterOperationAction(context =>
            {
                IEventAssignmentOperation assignment = (IEventAssignmentOperation)context.Operation;
                if (!(assignment.EventReference is IEventReferenceOperation reference)) return;
                HandlerAddedOrRemoved(context, subscriptions, unsubscribed, reference.Event.OriginalDefinition, reference.Instance, assignment.HandlerValue, assignment.Adds);
            }, OperationKind.EventAssignment);

            start.RegisterOperationAction(context =>
            {
                ICompoundAssignmentOperation assignment = (ICompoundAssignmentOperation)context.Operation;
                if (assignment.OperatorKind != BinaryOperatorKind.Add && assignment.OperatorKind != BinaryOperatorKind.Subtract) return;
                if (assignment.Target.Type?.TypeKind != TypeKind.Delegate) return;
                ISymbol member = ReferencedMember(assignment.Target, out IOperation instance);
                if (member != null) HandlerAddedOrRemoved(context, subscriptions, unsubscribed, member, instance, assignment.Value, assignment.OperatorKind == BinaryOperatorKind.Add);
            }, OperationKind.CompoundAssignment);

            start.RegisterOperationAction(context =>
            {
                IInvocationOperation call = (IInvocationOperation)context.Operation;
                string name = call.TargetMethod.Name;
                if (name != "AddListener" && name != "RemoveListener" && name != "RemoveAllListeners") return;
                if (!SymbolNames.IsOrDerivesFrom(call.TargetMethod.ContainingType, "UnityEngine.Events.UnityEventBase")) return;
                ISymbol member = ReferencedMember(call.Instance, out IOperation instance);
                if (member == null) return;
                if (name == "RemoveAllListeners")
                {
                    if (ContainingType(context.ContainingSymbol) is INamedTypeSymbol type) unsubscribed[Key(type, member, null)] = true;
                    return;
                }
                if (call.Arguments.Length > 0) HandlerAddedOrRemoved(context, subscriptions, unsubscribed, member, instance, call.Arguments[0].Value, name == "AddListener");
            }, OperationKind.Invocation);

            start.RegisterOperationAction(context =>
            {
                ISimpleAssignmentOperation assignment = (ISimpleAssignmentOperation)context.Operation;
                if (Unwrap(assignment.Target) is IFieldReferenceOperation field) fieldValues.AddAssignment(field.Field, IsThis(field.Instance), assignment.Value, context.ContainingSymbol);
            }, OperationKind.SimpleAssignment);

            start.RegisterOperationAction(context =>
            {
                IFieldInitializerOperation initializer = (IFieldInitializerOperation)context.Operation;
                foreach (IFieldSymbol field in initializer.InitializedFields) fieldValues.AddAssignment(field, true, initializer.Value, context.ContainingSymbol);
            }, OperationKind.FieldInitializer);

            // "TryGetComponent(out button)" into a field.
            start.RegisterOperationAction(context =>
            {
                IArgumentOperation argument = (IArgumentOperation)context.Operation;
                if (IsOutOrRef(argument) && Unwrap(argument.Value) is IFieldReferenceOperation field) fieldValues.AddAssignment(field.Field, IsThis(field.Instance), argument.Parent, context.ContainingSymbol);
            }, OperationKind.Argument);

            start.RegisterOperationAction(context => fieldValues.AddRead((IFieldReferenceOperation)context.Operation), OperationKind.FieldReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (Subscription subscription in subscriptions)
                {
                    if (unsubscribed.ContainsKey(subscription.Key) || unsubscribed.ContainsKey(subscription.AnyHandlerKey)) continue;
                    if (subscription.SourceFields != null && subscription.SourceFields.All(fieldValues.HoldsOwnHierarchy)) continue;
                    if (scope.IsAnalyzed(subscription.Location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, subscription.Location, subscription.EventName));
                }
            });
        }

        private static void HandlerAddedOrRemoved(OperationAnalysisContext context, ConcurrentBag<Subscription> subscriptions, ConcurrentDictionary<string, bool> unsubscribed,
            ISymbol member, IOperation instance, IOperation handlerValue, bool adds)
        {
            if (!(ContainingType(context.ContainingSymbol) is INamedTypeSymbol type)) return;
            ISymbol handler = Handler(handlerValue);
            if (handler == null) return;
            string key = Key(type, member, handler);
            if (!adds)
            {
                unsubscribed[key] = true;
                return;
            }
            if (IsOwnEvent(member, instance, type) || RunsOncePerDomain(context.ContainingSymbol)) return;
            if (IsOneShotCallback(member) || IsUiToolkitButtonClick(member)) return;

            List<IFieldSymbol> sourceFields = new List<IFieldSymbol>();
            bool fromOwnHierarchy = instance != null && IsFromOwnHierarchy(instance, type, sourceFields, NewLocalSet());
            if (fromOwnHierarchy && sourceFields.Count == 0) return;
            subscriptions.Add(new Subscription(key, Key(type, member, null), member.Name, context.Operation.Syntax.GetLocation(), fromOwnHierarchy ? sourceFields : null));
        }

        private static INamedTypeSymbol ContainingType(ISymbol symbol)
        {
            return symbol as INamedTypeSymbol ?? symbol?.ContainingType;
        }

        // Symbols can't be dictionary keys without a comparer, and a composite key would need three,
        // so the key is text. Original definitions, so a generic event matches itself across instances.
        private static string Key(INamedTypeSymbol type, ISymbol member, ISymbol handler)
        {
            return type.OriginalDefinition.ToDisplayString() + "|" + member.ToDisplayString() + "|" + (handler?.ToDisplayString() ?? "*");
        }

        // The event, field or property a subscription targets, and the object it is read from.
        private static ISymbol ReferencedMember(IOperation target, out IOperation instance)
        {
            while (target is IConversionOperation conversion) target = conversion.Operand;
            switch (target)
            {
                case IEventReferenceOperation reference:
                    instance = reference.Instance;
                    return reference.Event.OriginalDefinition;
                case IFieldReferenceOperation field:
                    instance = field.Instance;
                    return (ISymbol)(field.Field.AssociatedSymbol as IEventSymbol)?.OriginalDefinition ?? field.Field.OriginalDefinition;
                case IPropertyReferenceOperation property:
                    instance = property.Instance;
                    return property.Property.OriginalDefinition;
                default:
                    instance = null;
                    return null;
            }
        }

        // A method group, or a delegate kept somewhere it can be read again for the -=; null for a lambda.
        private static ISymbol Handler(IOperation value)
        {
            while (true)
            {
                switch (value)
                {
                    case IConversionOperation conversion: value = conversion.Operand; continue;
                    case IParenthesizedOperation parenthesized: value = parenthesized.Operand; continue;
                    case IDelegateCreationOperation creation: value = creation.Target; continue;
                    case IMethodReferenceOperation method: return method.Method.OriginalDefinition;
                    case IFieldReferenceOperation field: return field.Field.OriginalDefinition;
                    case IPropertyReferenceOperation property: return property.Property.OriginalDefinition;
                    case ILocalReferenceOperation local: return local.Local;
                    case IParameterReferenceOperation parameter: return parameter.Parameter;
                    default: return null;
                }
            }
        }

        // Reached through this (written or implied), or a static the type or a base of it declares.
        private static bool IsOwnEvent(ISymbol member, IOperation instance, INamedTypeSymbol type)
        {
            if (instance is IInstanceReferenceOperation) return true;
            if (instance != null) return false;
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, member.ContainingType?.OriginalDefinition)) return true;
            }
            return false;
        }

        private static bool RunsOncePerDomain(ISymbol containing)
        {
            if (!(containing is IMethodSymbol method)) return false;
            if (method.MethodKind == MethodKind.StaticConstructor) return true;
            return SymbolNames.HasAttribute(method, "UnityEngine.RuntimeInitializeOnLoadMethodAttribute")
                || SymbolNames.HasAttribute(method, "UnityEditor.InitializeOnLoadMethodAttribute");
        }

        private static bool IsOneShotCallback(ISymbol member)
        {
            if (member is IFieldSymbol field && field.Name == "delayCall") return SymbolNames.IsNamed(field.ContainingType, "UnityEditor.EditorApplication");
            if (!(member is IEventSymbol loadEvent)) return false;
            if (loadEvent.Name == "completed") return SymbolNames.IsOrDerivesFrom(loadEvent.ContainingType, "UnityEngine.AsyncOperation");
            return loadEvent.Name == "Completed"
                && (SymbolNames.IsNamed(loadEvent.ContainingType.OriginalDefinition, "UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle")
                    || SymbolNames.IsNamed(loadEvent.ContainingType.OriginalDefinition, "UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`1"));
        }

        private static bool IsUiToolkitButtonClick(ISymbol member)
        {
            return member is IEventSymbol click && click.Name == "clicked" && SymbolNames.IsOrDerivesFrom(click.ContainingType, "UnityEngine.UIElements.Button");
        }

        // Whether value is the component itself, its GameObject, a component on it or on one of its
        // children, or an object other than a Unity object created right there with new. Fields of the
        // component read along the way go into sourceFields, and the answer only holds if each of them
        // does too, which is known once every assignment is seen.
        // localsInProgress stops a local assigned from itself from looping.
        private static bool IsFromOwnHierarchy(IOperation value, INamedTypeSymbol type, List<IFieldSymbol> sourceFields, HashSet<ILocalSymbol> localsInProgress)
        {
            while (true)
            {
                value = Unwrap(value);
                switch (value)
                {
                    case IInstanceReferenceOperation _:
                        return IsThis(value) && SymbolNames.IsOrDerivesFrom(type, "UnityEngine.Component");
                    case IObjectCreationOperation creation:
                        return !SymbolNames.IsOrDerivesFrom(creation.Type, "UnityEngine.Object");
                    case IInvocationOperation call when IsOwnHierarchyLookup(call.TargetMethod):
                        value = call.Instance;
                        continue;
                    case IPropertyReferenceOperation property when IsOwnHierarchyProperty(property.Property):
                        value = property.Instance;
                        continue;
                    case IArrayElementReferenceOperation element:
                        value = element.ArrayReference;
                        continue;
                    case IFieldReferenceOperation field when IsThis(field.Instance):
                        sourceFields.Add(field.Field.OriginalDefinition);
                        return true;
                    case ILocalReferenceOperation local:
                        return LocalIsFromOwnHierarchy(local, type, sourceFields, localsInProgress);
                    default:
                        return false;
                }
            }
        }

        // Every value the local gets in its method has to qualify, and it has to get at least one. A
        // local holding an object made with new also has to keep it to itself.
        private static bool LocalIsFromOwnHierarchy(ILocalReferenceOperation reference, INamedTypeSymbol type, List<IFieldSymbol> sourceFields, HashSet<ILocalSymbol> localsInProgress)
        {
            if (!localsInProgress.Add(reference.Local)) return false;
            IOperation body = reference;
            while (body.Parent != null) body = body.Parent;

            bool assigned = false;
            bool created = false;
            foreach (IOperation operation in body.Descendants())
            {
                IOperation source = LocalValueSource(operation, reference.Local);
                if (source == null) continue;
                if (!IsFromOwnHierarchy(source, type, sourceFields, localsInProgress))
                {
                    assigned = false;
                    break;
                }
                assigned = true;
                created |= Unwrap(source) is IObjectCreationOperation;
            }
            localsInProgress.Remove(reference.Local);
            return assigned && !(created && LocalIsPassedOn(body, reference.Local));
        }

        // A lambda or local function that captures the local can keep its object alive, so any use
        // inside one counts.
        private static bool LocalIsPassedOn(IOperation body, ILocalSymbol local)
        {
            foreach (ILocalReferenceOperation use in body.Descendants().OfType<ILocalReferenceOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(use.Local, local)) continue;
                if (IsInsideFunction(use) || !OnlyUsesOrReplaces(use)) return true;
            }
            return false;
        }

        private static bool IsInsideFunction(IOperation operation)
        {
            for (IOperation parent = operation.Parent; parent != null; parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation || parent is ILocalFunctionOperation) return true;
            }
            return false;
        }

        // A read of a variable that calls the object's members or extension methods, compares it or
        // writes a new value, so the object itself goes nowhere else.
        private static bool OnlyUsesOrReplaces(IOperation reference)
        {
            IOperation read = reference;
            while (read.Parent is IConversionOperation || read.Parent is IParenthesizedOperation) read = read.Parent;
            switch (read.Parent)
            {
                case IMemberReferenceOperation member: return member.Instance == read;
                case IInvocationOperation call: return call.Instance == read;
                case IConditionalAccessOperation access: return access.Operation == read;
                case ISimpleAssignmentOperation assignment: return assignment.Target == read;
                case IBinaryOperation _:
                case IIsPatternOperation _:
                    return true;
                case IArgumentOperation argument:
                    return argument.Parent is IInvocationOperation extension && extension.TargetMethod.IsExtensionMethod && argument.Parameter?.Ordinal == 0;
                default:
                    return false;
            }
        }

        // What operation gives the local a value: its initializer, a foreach collection, the right
        // side of an assignment, or the call it's passed to as out or ref. Null when operation doesn't write it.
        private static IOperation LocalValueSource(IOperation operation, ILocalSymbol local)
        {
            switch (operation)
            {
                case IVariableDeclaratorOperation declarator when SymbolEqualityComparer.Default.Equals(declarator.Symbol, local):
                    return declarator.Parent is IForEachLoopOperation loop ? loop.Collection : declarator.Initializer?.Value;
                case ISimpleAssignmentOperation assignment when IsLocal(assignment.Target, local):
                    return assignment.Value;
                case IArgumentOperation argument when IsOutOrRef(argument) && IsLocal(argument.Value, local):
                    return argument.Parent;
                default:
                    return null;
            }
        }

        private static bool IsLocal(IOperation operation, ILocalSymbol local)
        {
            return Unwrap(operation) is ILocalReferenceOperation reference && SymbolEqualityComparer.Default.Equals(reference.Local, local);
        }

        private static bool IsOwnHierarchyLookup(IMethodSymbol method)
        {
            if (method.IsStatic || !OwnHierarchyLookups.Contains(method.Name)) return false;
            INamedTypeSymbol declaring = method.ContainingType.OriginalDefinition;
            return SymbolNames.IsNamed(declaring, "UnityEngine.Component")
                || SymbolNames.IsNamed(declaring, "UnityEngine.GameObject")
                || SymbolNames.IsNamed(declaring, "UnityEngine.Transform");
        }

        // ScrollRect.content: Unity's docs say it should be a child of the scroll view.
        private static bool IsOwnHierarchyProperty(IPropertySymbol property)
        {
            if (property.Name == "gameObject" || property.Name == "transform")
            {
                return SymbolNames.IsNamed(property.ContainingType, "UnityEngine.Component") || SymbolNames.IsNamed(property.ContainingType, "UnityEngine.GameObject");
            }
            return property.Name == "content" && SymbolNames.IsNamed(property.ContainingType, "UnityEngine.UI.ScrollRect");
        }

        private static bool IsOutOrRef(IArgumentOperation argument)
        {
            return argument.Parameter != null && (argument.Parameter.RefKind == RefKind.Out || argument.Parameter.RefKind == RefKind.Ref);
        }

        // this, written or implied, and not the object an initializer is filling in.
        private static bool IsThis(IOperation operation)
        {
            return operation is IInstanceReferenceOperation reference && reference.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance;
        }

        private static IOperation Unwrap(IOperation operation)
        {
            while (true)
            {
                switch (operation)
                {
                    case IConversionOperation conversion: operation = conversion.Operand; continue;
                    case IParenthesizedOperation parenthesized: operation = parenthesized.Operand; continue;
                    case IDeclarationExpressionOperation declaration: operation = declaration.Expression; continue;
                    default: return operation;
                }
            }
        }

        private static HashSet<ILocalSymbol> NewLocalSet()
        {
            return new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        }

        // Every value assigned to a component's fields, and which fields get read somewhere other than
        // their own members, to tell at compilation end which fields only ever hold the component's own
        // GameObject, something on it or under it, or an object it made and kept to itself.
        private sealed class ComponentFieldValues
        {
            // Per field, one list per qualifying assignment: the fields that assignment read its value from.
            private readonly ConcurrentDictionary<IFieldSymbol, ConcurrentBag<List<IFieldSymbol>>> ownHierarchyAssignments =
                new ConcurrentDictionary<IFieldSymbol, ConcurrentBag<List<IFieldSymbol>>>(SymbolEqualityComparer.Default);
            private readonly ConcurrentDictionary<IFieldSymbol, bool> otherAssignments = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            private readonly ConcurrentDictionary<IFieldSymbol, bool> createdAssignments = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            private readonly ConcurrentDictionary<IFieldSymbol, bool> passedOn = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

            public void AddAssignment(IFieldSymbol field, bool onThisObject, IOperation value, ISymbol containingSymbol)
            {
                field = field.OriginalDefinition;
                if (!IsComponentField(field) || IsNullOrDefault(value)) return;
                if (Unwrap(value) is IObjectCreationOperation) createdAssignments[field] = true;
                List<IFieldSymbol> sourceFields = new List<IFieldSymbol>();
                if (onThisObject && ContainingType(containingSymbol) is INamedTypeSymbol type && IsFromOwnHierarchy(value, type, sourceFields, NewLocalSet()))
                {
                    ownHierarchyAssignments.GetOrAdd(field, _ => new ConcurrentBag<List<IFieldSymbol>>()).Add(sourceFields);
                }
                else
                {
                    otherAssignments[field] = true;
                }
            }

            // Lambdas don't count here: they capture the component, not the object in the field.
            public void AddRead(IFieldReferenceOperation reference)
            {
                IFieldSymbol field = reference.Field.OriginalDefinition;
                if (IsComponentField(field) && (!IsThis(reference.Instance) || !OnlyUsesOrReplaces(reference))) passedOn[field] = true;
            }

            public bool HoldsOwnHierarchy(IFieldSymbol field)
            {
                return HoldsOwnHierarchy(field, new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default));
            }

            private static bool IsComponentField(IFieldSymbol field)
            {
                return !field.IsStatic && SymbolNames.IsOrDerivesFrom(field.ContainingType, "UnityEngine.Component");
            }

            // "a = null", "a = default", or "a = b = null", which clears the field rather than pointing it elsewhere.
            private static bool IsNullOrDefault(IOperation value)
            {
                value = Unwrap(value);
                while (value is ISimpleAssignmentOperation chained) value = Unwrap(chained.Value);
                return value is IDefaultValueOperation || (value != null && value.ConstantValue.HasValue && value.ConstantValue.Value == null);
            }

            // fieldsInProgress stops "child = child.GetChild(0)" from looping; such a field doesn't qualify.
            private bool HoldsOwnHierarchy(IFieldSymbol field, HashSet<IFieldSymbol> fieldsInProgress)
            {
                if (otherAssignments.ContainsKey(field) || Frameworks.Unity.IsSavedByUnity(field)) return false;
                if (createdAssignments.ContainsKey(field) && passedOn.ContainsKey(field)) return false;
                if (!ownHierarchyAssignments.TryGetValue(field, out ConcurrentBag<List<IFieldSymbol>> assignments)) return false;
                if (!fieldsInProgress.Add(field)) return false;
                bool holds = assignments.All(sourceFields => sourceFields.All(source => HoldsOwnHierarchy(source, fieldsInProgress)));
                fieldsInProgress.Remove(field);
                return holds;
            }
        }

        private sealed class Subscription
        {
            public Subscription(string key, string anyHandlerKey, string eventName, Location location, IReadOnlyList<IFieldSymbol> sourceFields)
            {
                Key = key;
                AnyHandlerKey = anyHandlerKey;
                EventName = eventName;
                Location = location;
                SourceFields = sourceFields;
            }

            public string Key { get; }
            public string AnyHandlerKey { get; }
            public string EventName { get; }
            public Location Location { get; }
            // Null when the event's object doesn't come from the subscriber's own GameObject. Otherwise
            // the fields it was read from, which have to hold objects from that GameObject too.
            public IReadOnlyList<IFieldSymbol> SourceFields { get; }
        }
    }
}
