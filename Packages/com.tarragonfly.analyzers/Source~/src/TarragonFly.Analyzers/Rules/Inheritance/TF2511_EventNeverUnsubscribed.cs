using System.Collections.Concurrent;
using System.Collections.Immutable;
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
    // and TF4100 report the attempts. Left alone: events the type declares itself or inherits,
    // reached through this or as its own statics, since the type raises them; and subscriptions in
    // a static constructor, a [RuntimeInitializeOnLoadMethod] or an [InitializeOnLoadMethod], which
    // run once per domain. A subscription meant to last for the object's whole life gets a
    // suppression with a reason. UI Toolkit's RegisterCallback isn't covered. Reported on the
    // subscription.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EventNeverUnsubscribedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2511",
            "Event subscribed but never unsubscribed",
            "'{0}' is subscribed here but never unsubscribed in this type; pair it in the matching lifecycle method",
            "Inheritance",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentBag<Subscription> subscriptions = new ConcurrentBag<Subscription>();
            // Keys are "type|event|handler"; "type|event|*" is a RemoveAllListeners on that event.
            ConcurrentDictionary<string, bool> unsubscribed = new ConcurrentDictionary<string, bool>();

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

            start.RegisterCompilationEndAction(context =>
            {
                foreach (Subscription subscription in subscriptions)
                {
                    if (unsubscribed.ContainsKey(subscription.Key) || unsubscribed.ContainsKey(subscription.AnyHandlerKey)) continue;
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
            subscriptions.Add(new Subscription(key, Key(type, member, null), member.Name, context.Operation.Syntax.GetLocation()));
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

        private sealed class Subscription
        {
            public Subscription(string key, string anyHandlerKey, string eventName, Location location)
            {
                Key = key;
                AnyHandlerKey = anyHandlerKey;
                EventName = eventName;
                Location = location;
            }

            public string Key { get; }
            public string AnyHandlerKey { get; }
            public string EventName { get; }
            public Location Location { get; }
        }
    }
}
