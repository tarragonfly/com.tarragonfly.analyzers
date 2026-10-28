using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7101: a static event or delegate field in runtime code that gets handlers but is never cleared
    // when Play Mode starts, as in "public static event Action<int> ScoreChanged;".
    //
    // With domain reload off, the handlers added in the last Play Mode session are still there. The
    // event then calls into objects that were destroyed (MissingReferenceException, or work done on
    // an object nobody can see), and each session adds another copy of every handler. Clear it with
    // "ScoreChanged = null;" in a static method marked
    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)].
    //
    // Checked: field-like static events and static delegate fields or auto-properties that aren't
    // readonly. They count as getting handlers when code in this assembly adds one with +=, or when
    // they aren't private, so other assemblies can. A clear is an assignment in a reset method, as
    // TF7100 describes. Unity 6000.6's [AutoStaticsCleanup] and [NoAutoStaticsCleanup] work as in
    // TF7100. Reported on the name. Runtime code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StaticEventNotClearedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7101",
            "Static event not cleared when Play Mode starts",
            "'{0}' keeps last session's handlers with domain reload off; set it to null in a [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] method",
            "StaticState",
            CodeKind.Runtime,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<ISymbol, bool> candidates = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> subscribed = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> cleared = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(context =>
            {
                ISymbol member = context.Symbol;
                if (!IsCandidate(member, context.CancellationToken) || PlayModeResets.NeedsNoReset(member)) return;
                candidates[member] = true;
                if (member.DeclaredAccessibility != Accessibility.Private) subscribed[member] = true;
            }, SymbolKind.Field, SymbolKind.Property, SymbolKind.Event);

            start.RegisterOperationAction(context =>
            {
                IEventAssignmentOperation assignment = (IEventAssignmentOperation)context.Operation;
                if (assignment.Adds && assignment.EventReference is IEventReferenceOperation reference && reference.Event.IsStatic) subscribed[reference.Event.OriginalDefinition] = true;
            }, OperationKind.EventAssignment);

            start.RegisterOperationAction(context =>
            {
                ICompoundAssignmentOperation assignment = (ICompoundAssignmentOperation)context.Operation;
                ISymbol member = AssignedMember(assignment.Target);
                if (member != null && member.IsStatic && assignment.OperatorKind == BinaryOperatorKind.Add) subscribed[member] = true;
            }, OperationKind.CompoundAssignment);

            start.RegisterOperationAction(context =>
            {
                ISimpleAssignmentOperation assignment = (ISimpleAssignmentOperation)context.Operation;
                ISymbol member = AssignedMember(assignment.Target);
                if (member != null && member.IsStatic && PlayModeResets.IsResetMethod(context.ContainingSymbol)) cleared[member] = true;
            }, OperationKind.SimpleAssignment);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (ISymbol member in candidates.Keys)
                {
                    if (!subscribed.ContainsKey(member) || cleared.ContainsKey(member)) continue;
                    Location location = member.Locations.FirstOrDefault(l => l.IsInSource);
                    if (location != null && scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, member.Name));
                }
            });
        }

        // The event, delegate field or property an assignment targets. Inside its own type a field-like
        // event can be assigned through its backing field.
        private static ISymbol AssignedMember(IOperation target)
        {
            switch (target)
            {
                case IEventReferenceOperation reference: return reference.Event.OriginalDefinition;
                case IFieldReferenceOperation field: return (ISymbol)(field.Field.AssociatedSymbol as IEventSymbol)?.OriginalDefinition ?? field.Field.OriginalDefinition;
                case IPropertyReferenceOperation property: return property.Property.OriginalDefinition;
                default: return null;
            }
        }

        private static bool IsCandidate(ISymbol member, CancellationToken ct)
        {
            if (!member.IsStatic || member.IsImplicitlyDeclared) return false;
            switch (member)
            {
                case IEventSymbol evt:
                    return evt.DeclaringSyntaxReferences.Any(r => r.GetSyntax(ct) is VariableDeclaratorSyntax);
                case IFieldSymbol field:
                    return !field.IsReadOnly && !field.IsConst && field.AssociatedSymbol == null && field.Type.TypeKind == TypeKind.Delegate;
                case IPropertySymbol property:
                    return property.SetMethod != null && property.Type.TypeKind == TypeKind.Delegate
                        && property.ContainingType.GetMembers().OfType<IFieldSymbol>().Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property));
                default:
                    return false;
            }
        }
    }
}