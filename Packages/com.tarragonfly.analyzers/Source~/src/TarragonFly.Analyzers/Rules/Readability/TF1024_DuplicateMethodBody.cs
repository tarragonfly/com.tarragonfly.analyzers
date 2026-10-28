using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1024: two methods of the same type with the same body, as in OnPlayerHit and OnPlayerFell both
    // resetting the combo and playing the same sound.
    //
    // A fix made in one copy is easily missed in the other. Have one call the other, or merge them.
    //
    // Bodies match when they are the same code apart from comments and layout, with at least two
    // statements, the parameters are written with the same types in the same order, and the return
    // types and "where" constraints are written the same, so a Task copy of a UniTask method isn't a
    // duplicate. Local functions and explicit interface implementations are compared too. Expression
    // bodies, constructors and accessors aren't checked, nor are methods in other types. Each method
    // or local function that repeats an earlier one in the type is reported on its name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DuplicateMethodBodyAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1024",
            "Methods with identical bodies",
            "'{0}' has the same body as '{1}'; have one call the other, or merge them",
            "Readability",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<ComparedMethod>> byType = new ConcurrentDictionary<INamedTypeSymbol, ConcurrentBag<ComparedMethod>>(SymbolEqualityComparer.Default);
            start.RegisterSyntaxNodeAction(context =>
            {
                MethodDeclarationSyntax declaration = context.Node as MethodDeclarationSyntax;
                LocalFunctionStatementSyntax local = context.Node as LocalFunctionStatementSyntax;
                BlockSyntax body = declaration != null ? declaration.Body : local.Body;
                if (!WorthComparing(body) || !(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is IMethodSymbol method)) return;
                // Bound now, so the semantic model doesn't stay alive until the compilation ends.
                ISymbol[] bindings = body.DescendantNodes().OfType<SimpleNameSyntax>()
                    .Select(name => context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol).ToArray();
                ComparedMethod compared = declaration != null
                    ? new ComparedMethod(method, bindings, declaration.Identifier, declaration.ReturnType, declaration.ParameterList, declaration.ConstraintClauses, body)
                    : new ComparedMethod(method, bindings, local.Identifier, local.ReturnType, local.ParameterList, local.ConstraintClauses, body);
                byType.GetOrAdd(method.ContainingType, _ => new ConcurrentBag<ComparedMethod>()).Add(compared);
            }, SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (ConcurrentBag<ComparedMethod> group in byType.Values)
                {
                    List<ComparedMethod> methods = group.ToList();
                    methods.Sort((a, b) =>
                    {
                        int byFile = string.CompareOrdinal(a.Body.SyntaxTree.FilePath, b.Body.SyntaxTree.FilePath);
                        return byFile != 0 ? byFile : a.Body.SpanStart.CompareTo(b.Body.SpanStart);
                    });

                    for (int i = 1; i < methods.Count; i++)
                    {
                        for (int j = 0; j < i; j++)
                        {
                            if (!methods[i].HasSameSignatureAs(methods[j]) || !SyntaxFactory.AreEquivalent(methods[i].Body, methods[j].Body)) continue;
                            if (!SameBindings(methods[i], methods[j])) continue;
                            Location location = methods[i].Name.GetLocation();
                            if (scope.IsAnalyzed(location.SourceTree)) context.ReportDiagnostic(Diagnostic.Create(Rule, location, methods[i].Name.ValueText, methods[j].Name.ValueText));
                            break;
                        }
                    }
                }
            });
        }

        // A method or local function as written.
        private sealed class ComparedMethod
        {
            public ComparedMethod(IMethodSymbol symbol, ISymbol[] bindings, SyntaxToken name, TypeSyntax returnType, ParameterListSyntax parameters, SyntaxList<TypeParameterConstraintClauseSyntax> constraints, BlockSyntax body)
            {
                Symbol = symbol;
                Bindings = bindings;
                Name = name;
                ReturnType = returnType;
                Parameters = parameters;
                Constraints = constraints;
                Body = body;
            }

            public SyntaxToken Name { get; }
            public IMethodSymbol Symbol { get; }
            // What each name in the body refers to, in source order.
            public ISymbol[] Bindings { get; }
            public TypeSyntax ReturnType { get; }
            public ParameterListSyntax Parameters { get; }
            public SyntaxList<TypeParameterConstraintClauseSyntax> Constraints { get; }
            public BlockSyntax Body { get; }

            public bool HasSameSignatureAs(ComparedMethod other)
            {
                if (!SyntaxFactory.AreEquivalent(ReturnType, other.ReturnType) || !SameParameterTypes(Parameters, other.Parameters)) return false;
                if (Constraints.Count != other.Constraints.Count) return false;
                for (int i = 0; i < Constraints.Count; i++)
                {
                    if (!SyntaxFactory.AreEquivalent(Constraints[i], other.Constraints[i])) return false;
                }
                return true;
            }
        }

        private static bool SameBindings(ComparedMethod first, ComparedMethod second)
        {
            if (first.Bindings.Length != second.Bindings.Length) return false;
            for (int i = 0; i < first.Bindings.Length; i++)
            {
                ISymbol firstSymbol = first.Bindings[i];
                ISymbol secondSymbol = second.Bindings[i];
                if (SymbolEqualityComparer.Default.Equals(firstSymbol, secondSymbol)) continue;
                if (firstSymbol is ITypeSymbol firstType && secondSymbol is ITypeSymbol secondType && SameType(firstType, secondType, first, second)) continue;
                if (firstSymbol is IParameterSymbol firstParameter && secondSymbol is IParameterSymbol secondParameter && firstParameter.Ordinal == secondParameter.Ordinal
                    && SymbolEqualityComparer.Default.Equals(firstParameter.ContainingSymbol, first.Symbol) && SymbolEqualityComparer.Default.Equals(secondParameter.ContainingSymbol, second.Symbol)
                    && SameType(firstParameter.Type, secondParameter.Type, first, second)) continue;
                if (firstSymbol is ILocalSymbol firstLocal && secondSymbol is ILocalSymbol secondLocal && SameType(firstLocal.Type, secondLocal.Type, first, second)
                    && firstLocal.DeclaringSyntaxReferences.Length == 1 && secondLocal.DeclaringSyntaxReferences.Length == 1)
                {
                    int declarationNodeIndex = DeclarationNodeIndex(first.Body, firstLocal.DeclaringSyntaxReferences[0]);
                    if (declarationNodeIndex >= 0 && declarationNodeIndex == DeclarationNodeIndex(second.Body, secondLocal.DeclaringSyntaxReferences[0])) continue;
                }
                // Lambda and local function parameters, local functions, query variables and labels
                // match when each body declares its own in the same place.
                if (!(firstSymbol is ILocalSymbol) && DeclaredAtSamePlace(firstSymbol?.OriginalDefinition, secondSymbol?.OriginalDefinition, first, second)) continue;
                if (firstSymbol is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol
                    && secondSymbol != null && SymbolEqualityComparer.Default.Equals(firstSymbol.OriginalDefinition, secondSymbol.OriginalDefinition)
                    && SameType(firstSymbol.ContainingType, secondSymbol.ContainingType, first, second))
                {
                    if (!(firstSymbol is IMethodSymbol firstCall) || !(secondSymbol is IMethodSymbol secondCall)
                        || firstCall.TypeArguments.Zip(secondCall.TypeArguments, (a, b) => SameType(a, b, first, second)).All(matches => matches)) continue;
                }
                return false;
            }
            return true;
        }

        private static bool SameType(ITypeSymbol firstType, ITypeSymbol secondType, ComparedMethod first, ComparedMethod second)
        {
            if (SymbolEqualityComparer.Default.Equals(firstType, secondType)) return true;
            if (firstType is ITypeParameterSymbol firstParameter && secondType is ITypeParameterSymbol secondParameter)
            {
                if (firstParameter.Ordinal != secondParameter.Ordinal) return false;
                // The compared method's own type parameters, those of the method around a compared
                // local function, or those of a local function declared in the same place in both bodies.
                return SymbolEqualityComparer.Default.Equals(firstParameter.ContainingSymbol, first.Symbol) && SymbolEqualityComparer.Default.Equals(secondParameter.ContainingSymbol, second.Symbol)
                    || SymbolEqualityComparer.Default.Equals(firstParameter.ContainingSymbol, first.Symbol.ContainingSymbol) && SymbolEqualityComparer.Default.Equals(secondParameter.ContainingSymbol, second.Symbol.ContainingSymbol)
                    || DeclaredAtSamePlace(firstParameter.ContainingSymbol, secondParameter.ContainingSymbol, first, second);
            }
            if (firstType is IArrayTypeSymbol firstArray && secondType is IArrayTypeSymbol secondArray)
                return firstArray.Rank == secondArray.Rank && SameType(firstArray.ElementType, secondArray.ElementType, first, second);
            if (firstType is IPointerTypeSymbol firstPointer && secondType is IPointerTypeSymbol secondPointer)
                return SameType(firstPointer.PointedAtType, secondPointer.PointedAtType, first, second);
            if (firstType is INamedTypeSymbol firstNamed && secondType is INamedTypeSymbol secondNamed
                && SymbolEqualityComparer.Default.Equals(firstNamed.OriginalDefinition, secondNamed.OriginalDefinition))
            {
                return SameType(firstNamed.ContainingType, secondNamed.ContainingType, first, second)
                    && firstNamed.TypeArguments.Zip(secondNamed.TypeArguments, (a, b) => SameType(a, b, first, second)).All(matches => matches);
            }
            return false;
        }

        private static bool DeclaredAtSamePlace(ISymbol firstSymbol, ISymbol secondSymbol, ComparedMethod first, ComparedMethod second)
        {
            if (firstSymbol == null || secondSymbol == null || firstSymbol.Kind != secondSymbol.Kind
                || firstSymbol.DeclaringSyntaxReferences.Length != 1 || secondSymbol.DeclaringSyntaxReferences.Length != 1) return false;
            int index = DeclarationNodeIndex(first.Body, firstSymbol.DeclaringSyntaxReferences[0]);
            return index >= 0 && index == DeclarationNodeIndex(second.Body, secondSymbol.DeclaringSyntaxReferences[0]);
        }

        // The bodies already match, so node order stays the same when comments or spacing change.
        private static int DeclarationNodeIndex(BlockSyntax body, SyntaxReference declaration)
        {
            if (declaration.SyntaxTree != body.SyntaxTree || !body.Span.Contains(declaration.Span)) return -1;
            int index = 0;
            foreach (SyntaxNode node in body.DescendantNodes())
            {
                if (node.Span == declaration.Span) return index;
                index++;
            }
            return -1;
        }

        // A block body of two or more statements; one-liners repeat on purpose too often to matter.
        private static bool WorthComparing(BlockSyntax body)
        {
            return body != null && body.Statements.Count >= 2;
        }

        private static bool SameParameterTypes(ParameterListSyntax first, ParameterListSyntax second)
        {
            if (first.Parameters.Count != second.Parameters.Count) return false;
            for (int i = 0; i < first.Parameters.Count; i++)
            {
                ParameterSyntax a = first.Parameters[i];
                ParameterSyntax b = second.Parameters[i];
                if (!SyntaxFactory.AreEquivalent(a.Type, b.Type) || a.Modifiers.ToString() != b.Modifiers.ToString()) return false;
            }
            return true;
        }
    }
}
