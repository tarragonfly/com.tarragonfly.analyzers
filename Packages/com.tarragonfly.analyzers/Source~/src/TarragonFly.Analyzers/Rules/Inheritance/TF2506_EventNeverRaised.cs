using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Inheritance
{
    // TF2506: an event that nothing ever raises, as in "public event Action Died;" when the death code
    // never calls Died?.Invoke().
    //
    // Listeners subscribe and wait for a call that never comes, so the UI or the audio hooked to it
    // silently does nothing.
    //
    // Only events declared as fields count, at any visibility, static ones included. An event counts as
    // raised when it is invoked, directly or with ?.Invoke, or handed on as a delegate value, such as
    // an argument or the handler in "other.Picked += Picked"; raising an override counts for the
    // virtual event it overrides. Assigning null and comparing with null don't count. Events with add
    // and remove accessors, abstract and override events, and events that implement an interface
    // aren't checked. UnityEvent fields aren't C# events and aren't checked either. Reported on the
    // event name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EventNeverRaisedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2506",
            "Event never raised",
            "Nothing raises '{0}', so its listeners never run; raise it or remove it",
            "Inheritance",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<IEventSymbol, bool> candidates = new ConcurrentDictionary<IEventSymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<IEventSymbol, bool> raised = new ConcurrentDictionary<IEventSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(context =>
            {
                IEventSymbol symbol = (IEventSymbol)context.Symbol;
                if (symbol.IsAbstract || symbol.IsOverride || !symbol.ExplicitInterfaceImplementations.IsEmpty || ImplementsInterface(symbol)) return;
                if (!symbol.DeclaringSyntaxReferences.All(r => r.GetSyntax(context.CancellationToken) is VariableDeclaratorSyntax)) return;
                candidates[symbol] = true;
            }, SymbolKind.Event);

            start.RegisterOperationAction(context =>
            {
                IEventReferenceOperation reference = (IEventReferenceOperation)context.Operation;
                if (!IsRaise(reference)) return;
                for (IEventSymbol raisedEvent = reference.Event.OriginalDefinition; raisedEvent != null; raisedEvent = raisedEvent.OverriddenEvent?.OriginalDefinition)
                {
                    raised[raisedEvent] = true;
                }
            }, OperationKind.EventReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (IEventSymbol symbol in candidates.Keys)
                {
                    if (raised.ContainsKey(symbol)) continue;
                    Location location = symbol.Locations[0];
                    if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, symbol.Name));
                }
            });
        }

        // Any use of the event's delegate other than subscribing, assigning null or a null check.
        private static bool IsRaise(IEventReferenceOperation reference)
        {
            if (NameOfExpressions.IsInsideNameOf(reference)) return false;
            IOperation current = reference;
            IOperation parent = reference.Parent;
            while (parent is IConversionOperation || parent is IParenthesizedOperation)
            {
                current = parent;
                parent = parent.Parent;
            }
            switch (parent)
            {
                case IEventAssignmentOperation subscription:
                    return subscription.HandlerValue == current;
                case ISimpleAssignmentOperation assignment:
                    return assignment.Target != reference;
                case IBinaryOperation comparison:
                    return !IsNull(comparison.LeftOperand) && !IsNull(comparison.RightOperand);
                case IIsPatternOperation _:
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsNull(IOperation operand)
        {
            while (operand is IConversionOperation conversion) operand = conversion.Operand;
            return operand.ConstantValue.HasValue && operand.ConstantValue.Value == null;
        }

        private static bool ImplementsInterface(IEventSymbol symbol)
        {
            foreach (INamedTypeSymbol type in symbol.ContainingType.AllInterfaces)
            {
                foreach (IEventSymbol member in type.GetMembers().OfType<IEventSymbol>())
                {
                    if (SymbolEqualityComparer.Default.Equals(symbol.ContainingType.FindImplementationForInterfaceMember(member), symbol)) return true;
                }
            }
            return false;
        }
    }
}
