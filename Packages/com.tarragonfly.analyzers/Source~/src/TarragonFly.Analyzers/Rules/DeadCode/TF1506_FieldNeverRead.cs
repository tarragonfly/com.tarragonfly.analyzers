using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1506: a private field, or a field of a private nested type, the code sets but never reads, as
    // in a "lastHitTime" field updated on every hit that nothing looks at, or a private event that is
    // subscribed to but never raised.
    //
    // The writes are wasted work, and the field suggests a feature that no longer exists. Remove it,
    // or read it where it was meant to be used.
    //
    // A field counts when something assigns it, increments it or adds to it, and nothing reads its
    // value; ++ and += don't count as reads, unless their result is used, as in "a = b = 0" (a void
    // method or lambda written as "=> hits = 0" uses nothing). An event counts when it is subscribed,
    // unsubscribed or cleared but never invoked or passed on. Fields set only by their initializer,
    // and fields with attributes, such as [SerializeField], which Unity reads, are skipped, as are
    // fields in [Serializable] types and in [StructLayout] types, where a field can be padding that
    // keeps the others in place. Static and readonly fields count. Reported on the field name.
    //
    // Fields of Unity objects, such as MonoBehaviours, are checked too. Their serialized fields stay
    // skipped through their attributes.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldNeverReadAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1506",
            "Private field never read",
            "'{0}' is set but never read; remove it, or use the value where it was meant to be used",
            "DeadCode",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<ISymbol, bool> candidates = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> written = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> read = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterSymbolAction(context =>
            {
                IFieldSymbol field = (IFieldSymbol)context.Symbol;
                if (FieldUses.IsCodeOnly(field)) candidates[field] = true;
            }, SymbolKind.Field);

            start.RegisterSymbolAction(context =>
            {
                IEventSymbol symbol = (IEventSymbol)context.Symbol;
                if (symbol.DeclaredAccessibility != Accessibility.Private || !symbol.GetAttributes().IsEmpty || symbol.Locations.Length == 0 || !symbol.Locations[0].IsInSource) return;
                if (symbol.DeclaringSyntaxReferences.All(r => r.GetSyntax(context.CancellationToken) is VariableDeclaratorSyntax)) candidates[symbol] = true;
            }, SymbolKind.Event);

            start.RegisterOperationAction(context =>
            {
                IFieldReferenceOperation reference = (IFieldReferenceOperation)context.Operation;
                IFieldSymbol field = reference.Field.OriginalDefinition;
                bool update = FieldUses.IsReadModifyWrite(reference) && !(reference.Parent is IArgumentOperation);
                bool writeOnly = (FieldUses.IsPlainWrite(reference) || update) && !FieldUses.IsWriteUsedAsValue(reference);
                if (writeOnly || FieldUses.IsPlainWrite(reference) || update) written[field] = true;
                if (!writeOnly) read[field] = true;
            }, OperationKind.FieldReference);

            start.RegisterOperationAction(context =>
            {
                IEventReferenceOperation reference = (IEventReferenceOperation)context.Operation;
                IEventSymbol symbol = reference.Event.OriginalDefinition;
                bool subscribes = reference.Parent is IEventAssignmentOperation
                    || (reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference);
                if (subscribes) written[symbol] = true;
                else read[symbol] = true;
            }, OperationKind.EventReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (ISymbol field in candidates.Keys)
                {
                    if (!written.ContainsKey(field) || read.ContainsKey(field)) continue;
                    Location location = field.Locations[0];
                    if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                }
            });
        }
    }
}