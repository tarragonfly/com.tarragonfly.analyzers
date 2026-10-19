using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1500: a private method, property, event, field, constant, nested type or local function that
    // nothing uses. Members of a private nested type count as private, whatever their own access. A
    // partial method counts through its declaration, implemented or not.
    //
    // Dead code still has to be read, kept compiling and searched past, and it often outlives the
    // feature it was written for. Remove it.
    //
    // Any mention by name counts as a use, including nameof() and a call from the member itself. Also
    // counted as used: Unity messages, such as Update, OnValidate, OnPostprocessAllAssets and
    // OnWillSaveAssets; members with an attribute, which covers [RuntimeInitializeOnLoadMethod],
    // [MenuItem], [ContextMenu], [Preserve], [UsedImplicitly], [SerializeField] and Odin's [Button];
    // and members an attribute names in a string, such as Odin's [ShowIf("IsVisible")] or
    // [ContextMenuItem("Reset", "ResetHealth")]. Constructors, overrides, interface implementations,
    // event handlers taking (object sender, EventArgs e), Deconstruct, GetEnumerator and GetAwaiter,
    // and fields of [Serializable] types aren't checked. Methods wired to a UnityEvent in a scene or
    // prefab are invisible to the code; mark them [UsedImplicitly]. Reported on the member name, or on
    // the whole field declaration when none of its fields is used.
    //
    // Members of Unity objects, such as MonoBehaviours, are checked too, with the Unity and Odin uses
    // above. And a string literal holding a member's name anywhere in
    // the assembly counts as a use, since Invoke("Respawn"), SendMessage and Odin attribute processors
    // call members by name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnusedPrivateMemberAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1500",
            "Unused private member",
            "'{0}' is never used; remove it",
            "DeadCode",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        private static readonly HashSet<string> PatternMethods = new HashSet<string> { "Deconstruct", "GetEnumerator", "GetAwaiter" };

        // UnityEditor.AssetModificationProcessor's callbacks, which the message catalog doesn't list.
        private static readonly HashSet<string> AssetModificationCallbacks = new HashSet<string>
        {
            "OnWillCreateAsset", "OnWillDeleteAsset", "OnWillMoveAsset", "OnWillSaveAssets", "IsOpenForEdit", "CanOpenForEdit", "MakeEditable", "OnStatusUpdated", "FileModeChanged"
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ConcurrentDictionary<ISymbol, bool> candidates = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            foreach (ISymbol symbol in start.Compilation.GetSymbolsWithName(_ => true, SymbolFilter.TypeAndMember, start.CancellationToken))
            {
                if (IsCandidate(symbol)) candidates[symbol] = true;
            }
            if (candidates.IsEmpty) return;
            HashSet<string> names = new HashSet<string>(candidates.Keys.Select(symbol => symbol.Name));
            ConcurrentDictionary<ISymbol, bool> used = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<string, bool> namesInStrings = new ConcurrentDictionary<string, bool>();

            start.RegisterSyntaxNodeAction(context =>
            {
                string text = ((LiteralExpressionSyntax)context.Node).Token.ValueText;
                if (text.Length > 1 && text[0] == '$') text = text.Substring(1);
                if (names.Contains(text)) namesInStrings[text] = true;
            }, SyntaxKind.StringLiteralExpression);

            start.RegisterSyntaxNodeAction(context =>
            {
                SimpleNameSyntax name = (SimpleNameSyntax)context.Node;
                if (!names.Contains(name.Identifier.ValueText)) return;
                SymbolInfo info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
                foreach (ISymbol symbol in info.Symbol != null ? new[] { info.Symbol } : info.CandidateSymbols.ToArray())
                {
                    ISymbol definition = symbol is IMethodSymbol method ? (method.ReducedFrom ?? method).OriginalDefinition : symbol.OriginalDefinition;
                    used[definition] = true;
                }
            }, SyntaxKind.IdentifierName, SyntaxKind.GenericName);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                LocalFunctionStatementSyntax local = (LocalFunctionStatementSyntax)context.Node;
                if (local.AttributeLists.Count > 0) return;
                SyntaxNode owner = local.Parent;
                while (owner != null && !(owner is MemberDeclarationSyntax) && !(owner is AnonymousFunctionExpressionSyntax)) owner = owner.Parent;
                if (owner == null || !(context.SemanticModel.GetDeclaredSymbol(local, context.CancellationToken) is IMethodSymbol symbol)) return;
                foreach (SimpleNameSyntax name in owner.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    if (name.Identifier.ValueText != symbol.Name) continue;
                    SymbolInfo info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
                    if (SymbolEqualityComparer.Default.Equals(info.Symbol, symbol) || info.CandidateSymbols.Contains(symbol, SymbolEqualityComparer.Default)) return;
                }
                context.ReportDiagnostic(Diagnostic.Create(Rule, local.Identifier.GetLocation(), symbol.Name));
            }), SyntaxKind.LocalFunctionStatement);

            start.RegisterCompilationEndAction(context =>
            {
                HashSet<ISymbol> unused = new HashSet<ISymbol>(candidates.Keys.Where(symbol => !used.ContainsKey(symbol) && !namesInStrings.ContainsKey(symbol.Name)), SymbolEqualityComparer.Default);
                foreach (INamedTypeSymbol type in unused.Select(symbol => symbol.ContainingType).Where(type => type != null).Distinct(SymbolEqualityComparer.Default).OfType<INamedTypeSymbol>().ToList())
                {
                    foreach (string referenced in NamesInAttributes(type)) unused.RemoveWhere(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingType, type) && symbol.Name == referenced);
                }

                HashSet<SyntaxNode> reportedDeclarations = new HashSet<SyntaxNode>();
                foreach (ISymbol symbol in unused)
                {
                    Location location = symbol.Locations[0];
                    if (!scope.IsAnalyzed(location.SourceTree)) continue;
                    SyntaxNode declaration = FieldDeclaration(symbol, context.CancellationToken);
                    if (declaration != null && AllUnused(declaration, symbol.ContainingType, unused))
                    {
                        if (reportedDeclarations.Add(declaration)) context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.GetLocation(), symbol.Name));
                        continue;
                    }
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, symbol.Name));
                }
            });
        }

        private static bool IsCandidate(ISymbol symbol)
        {
            if (!IsEffectivelyPrivate(symbol) || symbol.IsImplicitlyDeclared || symbol.Locations.Length == 0 || !symbol.Locations[0].IsInSource) return false;
            if (!symbol.GetAttributes().IsEmpty || Frameworks.Unity.HasImplicitUseAttribute(symbol) || ImplementsInterface(symbol)) return false;
            switch (symbol)
            {
                case IMethodSymbol method:
                    if (method.MethodKind != MethodKind.Ordinary || !method.ExplicitInterfaceImplementations.IsEmpty || method.IsOverride || method.IsVirtual || method.IsAbstract) return false;
                    if (PatternMethods.Contains(method.Name) || method.PartialDefinitionPart != null || IsEventHandler(method)) return false;
                    return !IsUnityCallback(method);
                case IPropertySymbol property:
                    return property.ExplicitInterfaceImplementations.IsEmpty && !property.IsOverride && !property.IsVirtual && !property.IsAbstract && !property.IsIndexer;
                case IEventSymbol eventSymbol:
                    return eventSymbol.ExplicitInterfaceImplementations.IsEmpty && !eventSymbol.IsOverride && !eventSymbol.IsVirtual && !eventSymbol.IsAbstract;
                case IFieldSymbol field:
                    return !SymbolNames.HasAttribute(field.ContainingType, "System.SerializableAttribute");
                case INamedTypeSymbol type:
                    return type.ContainingType != null;
                default:
                    return false;
            }
        }

        private static bool IsUnityCallback(IMethodSymbol method)
        {
            INamedTypeSymbol type = method.ContainingType;
            if (type == null) return false;
            if (new ScriptInfo(type).IsMessage(method)) return true;
            return AssetModificationCallbacks.Contains(method.Name) && SymbolNames.IsOrDerivesFrom(type, "UnityEditor.AssetModificationProcessor");
        }

        // (object sender, SomeEventArgs e), which designers and reflection wire up by name.
        private static bool IsEventHandler(IMethodSymbol method)
        {
            return method.Parameters.Length == 2 && method.Parameters[0].Type.SpecialType == SpecialType.System_Object
                && SymbolNames.IsOrDerivesFrom(method.Parameters[1].Type, "System.EventArgs");
        }

        // Private itself, or reachable only through a private containing type.
        private static bool IsEffectivelyPrivate(ISymbol symbol)
        {
            if (symbol.DeclaredAccessibility == Accessibility.Private) return true;
            for (INamedTypeSymbol type = symbol.ContainingType; type != null; type = type.ContainingType)
            {
                if (type.DeclaredAccessibility == Accessibility.Private) return true;
            }
            return false;
        }

        private static bool ImplementsInterface(ISymbol symbol)
        {
            INamedTypeSymbol type = symbol.ContainingType;
            if (type == null || symbol is INamedTypeSymbol) return false;
            foreach (INamedTypeSymbol contract in type.AllInterfaces)
            {
                foreach (ISymbol member in contract.GetMembers())
                {
                    if (SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), symbol)) return true;
                }
            }
            return false;
        }

        // Member names that attributes on the type or its members name in strings.
        private static IEnumerable<string> NamesInAttributes(INamedTypeSymbol type)
        {
            foreach (ISymbol member in new ISymbol[] { type }.Concat(type.GetMembers()))
            {
                foreach (AttributeData attribute in member.GetAttributes())
                {
                    foreach (string name in Odin.GetReferencedMemberNames(attribute)) yield return name;
                    foreach (string name in Frameworks.Unity.GetMethodNamesReferencedBy(attribute)) yield return name;
                }
            }
        }

        private static SyntaxNode FieldDeclaration(ISymbol symbol, System.Threading.CancellationToken ct)
        {
            if (!(symbol is IFieldSymbol) && !(symbol is IEventSymbol)) return null;
            SyntaxNode declarator = symbol.DeclaringSyntaxReferences[0].GetSyntax(ct);
            return declarator is VariableDeclaratorSyntax ? declarator.Parent?.Parent : null;
        }

        private static bool AllUnused(SyntaxNode declaration, INamedTypeSymbol type, HashSet<ISymbol> unused)
        {
            VariableDeclarationSyntax variables = declaration is BaseFieldDeclarationSyntax field ? field.Declaration : null;
            if (variables == null) return false;
            return variables.Variables.All(v => unused.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol.ContainingType, type) && symbol.Name == v.Identifier.ValueText));
        }
    }
}