using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Async
{
    // TF5000: a disposable object, such as a CancellationTokenSource, that is created and never
    // disposed, as in "fadeCts = new CancellationTokenSource();" in a class that never calls
    // fadeCts.Dispose().
    //
    // A CancellationTokenSource that is never disposed keeps its timer and registrations alive, and
    // file or network handles stay open until the garbage collector gets to them.
    //
    // Checked: locals and private fields set to a "new" disposable object, or to
    // CancellationTokenSource.CreateLinkedTokenSource(...). The object is handled when it is disposed
    // or closed anywhere (Dispose, DisposeAsync or Close, ?. included), used in a using, passed to a
    // method or constructor, returned, or stored somewhere else, also through "?:" or "??"; handing a
    // token source to an owner such as AudioManager.StoreMusicFadeCts(cts) counts. A lambda that only
    // uses it doesn't. MemoryStream, StringReader, StringWriter, tasks, the render pipelines'
    // PropertyFetcher and ShaderGraph's ShaderStringBuilder hold nothing that needs disposing (the last
    // two are IDisposable only for using blocks) and aren't checked. Neither is SemaphoreSlim, whose
    // Dispose only frees the wait handle that reading AvailableWaitHandle creates. Nor are objects whose Dispose() is
    // empty in source; an UnsafeList<T> made from a pointer, which only views memory the caller owns,
    // such as stackalloc memory; native collections made with Allocator.Temp, which Unity frees at the
    // end of the frame; and [DeallocateOnJobCompletion] fields, which the job system frees. Any other
    // object made from a pointer is checked, since its Dispose may free that memory. Reported on each
    // creation.
    //
    // System.Threading.Timer is checked too; an undisposed Timer keeps firing.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NeverDisposedAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF5000",
            "Disposable object never disposed",
            "'{0}' is created here but never disposed; dispose it when done, or use a using",
            "Async",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        private static readonly string[] NothingToDispose =
        {
            "System.IO.MemoryStream", "System.IO.StringReader", "System.IO.StringWriter",
            "System.Threading.SemaphoreSlim", "System.Threading.Tasks.Task", "System.Threading.Tasks.Task`1", "System.Threading.Tasks.ValueTask", "System.Threading.Tasks.ValueTask`1",
            "UnityEditor.Rendering.PropertyFetcher`1", "UnityEditor.ShaderGraph.ShaderStringBuilder"
        };

        private static readonly HashSet<string> DisposeMethods = new HashSet<string> { "Dispose", "DisposeAsync", "Close" };

        private static readonly string[] PointerViews =
        {
            "Unity.Collections.LowLevel.Unsafe.UnsafeList`1"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<IFieldSymbol, ConcurrentBag<SyntaxNode>> fieldCreations = new ConcurrentDictionary<IFieldSymbol, ConcurrentBag<SyntaxNode>>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<IFieldSymbol, bool> fieldsDisposedOrHandedOn = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterOperationAction(context =>
            {
                IFieldReferenceOperation reference = (IFieldReferenceOperation)context.Operation;
                IFieldSymbol field = reference.Field.OriginalDefinition;
                if (field.DeclaredAccessibility != Accessibility.Private || field.IsStatic) return;
                if (SymbolNames.HasAttribute(field, "Unity.Collections.DeallocateOnJobCompletionAttribute")) return;
                if (reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference)
                {
                    if (IsCreation(assignment.Value)) fieldCreations.GetOrAdd(field, _ => new ConcurrentBag<SyntaxNode>()).Add(assignment.Syntax);
                    return;
                }
                if (IsDisposedOrHandedOn(reference)) fieldsDisposedOrHandedOn[field] = true;
            }, OperationKind.FieldReference);

            start.RegisterOperationAction(context =>
            {
                IFieldInitializerOperation initializer = (IFieldInitializerOperation)context.Operation;
                if (!IsCreation(initializer.Value)) return;
                foreach (IFieldSymbol field in initializer.InitializedFields)
                {
                    if (field.DeclaredAccessibility != Accessibility.Private || field.IsStatic) continue;
                    if (SymbolNames.HasAttribute(field, "Unity.Collections.DeallocateOnJobCompletionAttribute")) continue;
                    SyntaxNode declarator = field.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken);
                    fieldCreations.GetOrAdd(field, _ => new ConcurrentBag<SyntaxNode>()).Add(declarator);
                }
            }, OperationKind.FieldInitializer);

            start.RegisterOperationBlockAction(context =>
            {
                foreach (IOperation root in context.OperationBlocks)
                {
                    if (!scope.IsAnalyzed(root.Syntax.SyntaxTree)) continue;
                    ReportLocals(context, root);
                }
            });

            start.RegisterCompilationEndAction(context =>
            {
                foreach (KeyValuePair<IFieldSymbol, ConcurrentBag<SyntaxNode>> entry in fieldCreations)
                {
                    if (fieldsDisposedOrHandedOn.ContainsKey(entry.Key)) continue;
                    foreach (SyntaxNode creation in entry.Value.OrderBy(node => node.SpanStart))
                    {
                        if (scope.IsAnalyzed(creation.SyntaxTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), entry.Key.Name));
                    }
                }
            });
        }

        private static void ReportLocals(OperationBlockAnalysisContext context, IOperation root)
        {
            Dictionary<ILocalSymbol, List<SyntaxNode>> creations = new Dictionary<ILocalSymbol, List<SyntaxNode>>(SymbolEqualityComparer.Default);
            HashSet<ILocalSymbol> handled = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            foreach (IOperation operation in root.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case IVariableDeclaratorOperation declarator when declarator.Initializer != null && IsCreation(declarator.Initializer.Value):
                        SyntaxNode statement = declarator.Syntax.Parent?.Parent;
                        bool usingVariable = statement is UsingStatementSyntax || statement is LocalDeclarationStatementSyntax local && !local.UsingKeyword.IsKind(SyntaxKind.None);
                        if (usingVariable) handled.Add(declarator.Symbol);
                        Add(creations, declarator.Symbol, declarator.Syntax);
                        break;
                    case ISimpleAssignmentOperation assignment when assignment.Target is ILocalReferenceOperation target && IsCreation(assignment.Value):
                        Add(creations, target.Local, assignment.Syntax);
                        break;
                    case ILocalReferenceOperation reference when IsDisposedOrHandedOn(reference):
                        handled.Add(reference.Local);
                        break;
                }
            }
            foreach (KeyValuePair<ILocalSymbol, List<SyntaxNode>> entry in creations)
            {
                if (handled.Contains(entry.Key)) continue;
                foreach (SyntaxNode creation in entry.Value) context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), entry.Key.Name));
            }
        }

        private static void Add(Dictionary<ILocalSymbol, List<SyntaxNode>> creations, ILocalSymbol local, SyntaxNode creation)
        {
            if (!creations.TryGetValue(local, out List<SyntaxNode> list)) creations[local] = list = new List<SyntaxNode>();
            list.Add(creation);
        }

        // A view made from a pointer to memory the caller owns, which it never frees.
        private static bool IsPointerView(IObjectCreationOperation creation)
        {
            return creation.Arguments.Any(argument => argument.Value.Type is IPointerTypeSymbol)
                && PointerViews.Any(name => SymbolNames.IsNamed(creation.Type?.OriginalDefinition, name));
        }

        // "new X(...)" of a type that needs disposing, or CancellationTokenSource.CreateLinkedTokenSource(...).
        private static bool IsCreation(IOperation value)
        {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            ITypeSymbol type;
            switch (value)
            {
                case IObjectCreationOperation creation:
                    if (creation.Arguments.Any(argument => IsTempAllocator(argument.Value)) || IsPointerView(creation)) return false;
                    type = creation.Type;
                    break;
                case IInvocationOperation call when call.TargetMethod.Name == "CreateLinkedTokenSource":
                    type = call.Type;
                    break;
                default:
                    return false;
            }
            if (type == null || NothingToDispose.Any(name => SymbolNames.IsNamed(type.OriginalDefinition, name))) return false;
            if (!SymbolNames.Implements(type, candidate => SymbolNames.IsNamed(candidate, "System.IDisposable"))) return false;
            return !HasEmptyDispose(type, value.SemanticModel?.Compilation);
        }

        // Allocator.Temp, whose allocations Unity frees at the end of the frame.
        private static bool IsTempAllocator(IOperation argument)
        {
            while (argument is IConversionOperation conversion) argument = conversion.Operand;
            return argument is IFieldReferenceOperation reference && reference.Field.Name == "Temp"
                && SymbolNames.IsNamed(reference.Field.ContainingType, "Unity.Collections.Allocator");
        }

        // A Dispose() written in this compilation with nothing in it. One from another assembly can't
        // be looked into, so it counts as doing something.
        private static bool HasEmptyDispose(ITypeSymbol type, Compilation compilation)
        {
            if (!(compilation?.GetSpecialType(SpecialType.System_IDisposable).GetMembers("Dispose").FirstOrDefault() is IMethodSymbol contract)) return false;
            if (!(type.FindImplementationForInterfaceMember(contract) is IMethodSymbol dispose)) return false;
            // The interface maps to the virtual method; an override further down is what runs.
            for (ITypeSymbol current = type; current != null && (dispose.IsVirtual || dispose.IsOverride); current = current.BaseType)
            {
                IMethodSymbol own = current.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault(method => method.Parameters.IsEmpty && method.IsOverride);
                if (own == null) continue;
                dispose = own;
                break;
            }
            return dispose.DeclaringSyntaxReferences.Length > 0 && dispose.DeclaringSyntaxReferences[0].GetSyntax() is BaseMethodDeclarationSyntax declaration
                && declaration.ExpressionBody == null && declaration.Body != null && declaration.Body.Statements.Count == 0;
        }

        // Disposed, closed, used in a using, passed on, returned or stored somewhere else.
        private static bool IsDisposedOrHandedOn(IOperation reference)
        {
            IOperation current = reference;
            IOperation parent = current.Parent;
            while (parent is IConversionOperation || parent is IParenthesizedOperation || parent is ICoalesceOperation
                || (parent is IConditionalOperation conditional && conditional.Condition != current))
            {
                current = parent;
                parent = parent.Parent;
            }
            switch (parent)
            {
                case IInvocationOperation call:
                    return call.Instance == current && DisposeMethods.Contains(call.TargetMethod.Name);
                case IConditionalAccessOperation conditional:
                    return conditional.Operation == current && conditional.WhenNotNull is IInvocationOperation disposeCall && DisposeMethods.Contains(disposeCall.TargetMethod.Name);
                case IArgumentOperation _:
                case IReturnOperation _:
                case IUsingOperation _:
                case IArrayInitializerOperation _:
                case ITupleOperation _:
                case IVariableInitializerOperation _:
                    return true;
                case ISimpleAssignmentOperation assignment:
                    return assignment.Value == current;
                default:
                    return false;
            }
        }
    }
}