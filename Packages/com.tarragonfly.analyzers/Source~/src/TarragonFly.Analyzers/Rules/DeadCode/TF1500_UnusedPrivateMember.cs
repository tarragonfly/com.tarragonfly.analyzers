using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Frameworks;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Rules.DeadCode
{
    // TF1500: a private method, property, indexer, event, field, constant, nested type or local
    // function that nothing uses. Members of a private nested type count as private, whatever their
    // own access. A partial method counts through its declaration, implemented or not. Accessors are
    // checked on their own too: a private accessor of a wider property, or an accessor of a private
    // property, that nothing uses, meaning a setter nothing writes through or a getter nothing reads.
    // The getter of an auto-property is left alone unless it's private itself, since the property
    // can't do without it. An initializer doesn't go through the setter, so the private setter in
    // "{ get; private set; } = new List<Card>();" is reported when nothing else sets it; the property
    // works get-only.
    //
    // Dead code still has to be read, kept compiling and searched past, and it often outlives the
    // feature it was written for. Remove it.
    //
    // Any mention by name counts as a use, including nameof() and a call from the member itself; for
    // accessors, nameof() counts as both a read and a write. Also counted as used: Unity messages, such
    // as Update, OnValidate, OnPostprocessAllAssets and OnWillSaveAssets, plus EditorWindow.ShowButton,
    // OnDidApplyAnimationProperties and __internalAwake; animation event receivers, which are methods
    // of a MonoBehaviour taking one AnimationEvent; members with an attribute, which covers
    // [RuntimeInitializeOnLoadMethod], [MenuItem], [ContextMenu], [Preserve], [UsedImplicitly],
    // [SerializeField] and Odin's [Button]; members an attribute names in a string, such as Odin's
    // [ShowIf("IsVisible")], [ContextMenuItem("Reset", "ResetHealth")] or [DebuggerDisplay("{Label()}")];
    // nested AssetPostprocessor and AssetModificationProcessor types, which Unity creates itself; and
    // nested types holding a static method with an attribute, such as a [SettingsProvider] method.
    // Constructors, overrides, interface implementations, event handlers taking (object sender,
    // EventArgs e), Deconstruct, GetEnumerator and GetAwaiter, enum members, which Enum.GetNames,
    // casts and saved numbers reach without their names, and fields of [Serializable] types aren't
    // checked; nor are the accessors of a property with an attribute or a [field: ...] one. Methods
    // wired to a UnityEvent in a scene or prefab are invisible to the code; mark them
    // [UsedImplicitly]. Reported on the member name, on the accessor, or on the whole field
    // declaration when none of its fields is used.
    //
    // Members of Unity objects, such as MonoBehaviours, are checked too, with the Unity and Odin uses
    // above. And a string literal holding a member's name anywhere in the assembly counts as a use,
    // since Invoke("Respawn"), SendMessage and Odin attribute processors call members by name.
    //
    // Code in an inactive #if branch isn't compiled, so a use there isn't seen and a member used only
    // there is reported. Put the member under the same #if, or mark it [UsedImplicitly].
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnusedPrivateMemberAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1500",
            "Unused private member",
            "{0} is never used; remove it",
            "DeadCode",
            CodeKind.All,
            extraTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        private static readonly HashSet<string> PatternMethods = new HashSet<string> { "Deconstruct", "GetEnumerator", "GetAwaiter" };

        // UnityEditor.AssetModificationProcessor's callbacks, which the message catalog doesn't list.
        private static readonly HashSet<string> AssetModificationCallbacks = new HashSet<string>
        {
            "OnWillCreateAsset", "OnWillDeleteAsset", "OnWillMoveAsset", "OnWillSaveAssets", "IsOpenForEdit", "CanOpenForEdit", "MakeEditable", "OnStatusUpdated", "FileModeChanged"
        };

        // A member name inside the braces of a [DebuggerDisplay] string, such as Label in "{Label(),nq}".
        private static readonly Regex DebuggerDisplayName = new Regex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(ReportUnusedLocalFunction), SyntaxKind.LocalFunctionStatement);

            ConcurrentDictionary<ISymbol, bool> candidates = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            List<IMethodSymbol> accessors = new List<IMethodSymbol>();
            foreach (ISymbol symbol in start.Compilation.GetSymbolsWithName(_ => true, SymbolFilter.TypeAndMember, start.CancellationToken))
            {
                bool candidate = IsCandidate(symbol);
                if (candidate) candidates[symbol] = true;
                if (symbol is IPropertySymbol property) accessors.AddRange(CheckedAccessors(property, candidate));
            }
            if (candidates.IsEmpty && accessors.Count == 0) return;
            HashSet<string> names = new HashSet<string>(candidates.Keys.Concat(accessors.Select(accessor => accessor.AssociatedSymbol)).Select(symbol => symbol.Name));
            ConcurrentDictionary<ISymbol, bool> used = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> read = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
            ConcurrentDictionary<ISymbol, bool> written = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
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
                    if (definition is IPropertySymbol property) RecordAccess(property, name, read, written);
                }
            }, SyntaxKind.IdentifierName, SyntaxKind.GenericName);

            if (names.Contains(WellKnownMemberNames.Indexer))
            {
                start.RegisterSyntaxNodeAction(context =>
                {
                    if (!(context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken).Symbol is IPropertySymbol indexer)) return;
                    used[indexer.OriginalDefinition] = true;
                    RecordAccess((IPropertySymbol)indexer.OriginalDefinition, (ExpressionSyntax)context.Node, read, written);
                }, SyntaxKind.ElementAccessExpression, SyntaxKind.ImplicitElementAccess);
            }

            start.RegisterCompilationEndAction(context =>
            {
                HashSet<ISymbol> unused = new HashSet<ISymbol>(candidates.Keys.Where(symbol => !used.ContainsKey(symbol) && !namesInStrings.ContainsKey(symbol.Name)), SymbolEqualityComparer.Default);
                Dictionary<INamedTypeSymbol, HashSet<string>> namesInAttributes = new Dictionary<INamedTypeSymbol, HashSet<string>>(SymbolEqualityComparer.Default);
                foreach (INamedTypeSymbol type in unused.Concat(accessors).Select(symbol => symbol.ContainingType).Where(type => type != null).Distinct(SymbolEqualityComparer.Default).OfType<INamedTypeSymbol>())
                {
                    namesInAttributes[type] = new HashSet<string>(NamesInAttributes(type));
                }
                unused.RemoveWhere(symbol => symbol.ContainingType != null && namesInAttributes[symbol.ContainingType].Contains(symbol.Name));

                HashSet<SyntaxNode> reportedDeclarations = new HashSet<SyntaxNode>();
                foreach (ISymbol symbol in unused)
                {
                    Location location = symbol.Locations[0];
                    if (!scope.IsAnalyzed(location.SourceTree)) continue;
                    SyntaxNode declaration = FieldDeclaration(symbol, context.CancellationToken);
                    if (declaration != null && AllUnused(declaration, symbol.ContainingType, unused))
                    {
                        if (reportedDeclarations.Add(declaration)) context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.GetLocation(), $"'{symbol.Name}'"));
                        continue;
                    }
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, $"'{symbol.Name}'"));
                }

                foreach (IMethodSymbol accessor in accessors)
                {
                    IPropertySymbol property = (IPropertySymbol)accessor.AssociatedSymbol;
                    if (unused.Contains(property) || namesInStrings.ContainsKey(property.Name) || namesInAttributes[accessor.ContainingType].Contains(property.Name)) continue;
                    bool getter = accessor.MethodKind == MethodKind.PropertyGet;
                    if (getter ? read.ContainsKey(property) : written.ContainsKey(property)) continue;
                    SyntaxNode declaration = accessor.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken);
                    if (!scope.IsAnalyzed(declaration.SyntaxTree)) continue;
                    string label = property.IsIndexer ? "this[]" : property.Name;
                    context.ReportDiagnostic(Diagnostic.Create(Rule, declaration.GetLocation(), $"The {(getter ? "get" : "set")} accessor of '{label}'"));
                }
            });
        }

        private static void ReportUnusedLocalFunction(SyntaxNodeAnalysisContext context)
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
                // A generic local function is used through a constructed copy, such as Find<Enemy>.
                if (SymbolEqualityComparer.Default.Equals(info.Symbol?.OriginalDefinition, symbol)) return;
                if (info.CandidateSymbols.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, symbol))) return;
            }
            context.ReportDiagnostic(Diagnostic.Create(Rule, local.Identifier.GetLocation(), $"'{symbol.Name}'"));
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
                    return !IsUnityCallback(method) && !IsAnimationEventReceiver(method);
                case IPropertySymbol property:
                    return property.ExplicitInterfaceImplementations.IsEmpty && !property.IsOverride && !property.IsVirtual && !property.IsAbstract;
                case IEventSymbol eventSymbol:
                    return eventSymbol.ExplicitInterfaceImplementations.IsEmpty && !eventSymbol.IsOverride && !eventSymbol.IsVirtual && !eventSymbol.IsAbstract;
                case IFieldSymbol field:
                    return field.ContainingType.TypeKind != TypeKind.Enum && !SymbolNames.HasAttribute(field.ContainingType, "System.SerializableAttribute");
                case INamedTypeSymbol type:
                    return type.ContainingType != null && !IsCreatedByUnity(type) && !HoldsAttributedStaticMethod(type);
                default:
                    return false;
            }
        }

        // The accessors to judge on their own: every accessor of a property that is a candidate, and
        // the private accessors of a wider one. An auto-property's getter is left out unless it's
        // private itself, since the property can't do without one.
        private static IEnumerable<IMethodSymbol> CheckedAccessors(IPropertySymbol property, bool propertyIsCandidate)
        {
            if (property.IsAbstract || property.IsOverride || !property.ExplicitInterfaceImplementations.IsEmpty || property.IsImplicitlyDeclared) yield break;
            if (property.Locations.Length == 0 || !property.Locations[0].IsInSource || !property.GetAttributes().IsEmpty || BackingFieldHasAttributes(property)) yield break;
            foreach (IMethodSymbol accessor in new[] { property.GetMethod, property.SetMethod })
            {
                if (accessor == null || accessor.IsImplicitlyDeclared || accessor.DeclaringSyntaxReferences.Length == 0 || !accessor.GetAttributes().IsEmpty) continue;
                bool privateAccessor = accessor.DeclaredAccessibility == Accessibility.Private && property.DeclaredAccessibility != Accessibility.Private;
                if (!propertyIsCandidate && !privateAccessor) continue;
                if (accessor.MethodKind == MethodKind.PropertyGet && !privateAccessor && IsAutoAccessor(accessor)) continue;
                yield return accessor;
            }
        }

        private static bool IsAutoAccessor(IMethodSymbol accessor)
        {
            return accessor.DeclaringSyntaxReferences[0].GetSyntax() is AccessorDeclarationSyntax declaration && declaration.Body == null && declaration.ExpressionBody == null;
        }

        // [field: SerializeField] and the like land on the backing field, and Unity writes that field
        // without the setter.
        private static bool BackingFieldHasAttributes(IPropertySymbol property)
        {
            foreach (IFieldSymbol field in property.ContainingType.GetMembers().OfType<IFieldSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property) && !field.GetAttributes().IsEmpty) return true;
            }
            return false;
        }

        // Records whether a use of a property or indexer reads it, writes it, or both.
        private static void RecordAccess(IPropertySymbol property, ExpressionSyntax use, ConcurrentDictionary<ISymbol, bool> read, ConcurrentDictionary<ISymbol, bool> written)
        {
            ExpressionSyntax node = use;
            if (node.Parent is MemberAccessExpressionSyntax access && access.Name == node) node = access;
            while (node.Parent is ParenthesizedExpressionSyntax parenthesized) node = parenthesized;

            bool reads = true;
            bool writes = false;
            switch (node.Parent)
            {
                case AssignmentExpressionSyntax assignment when assignment.Left == node:
                    writes = true;
                    reads = !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
                    break;
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                    writes = true;
                    break;
                case ArgumentSyntax argument when IsDeconstructionTarget(argument):
                    writes = true;
                    reads = false;
                    break;
                case ArgumentSyntax argument when NameOfExpressions.IsNameOf(argument.Parent?.Parent):
                    writes = true;
                    break;
            }
            // Writing through a ref-returning property calls its getter.
            if (property.ReturnsByRef || property.ReturnsByRefReadonly) reads = true;
            if (reads) read[property] = true;
            if (writes) written[property] = true;
        }

        // An element of the tuple on the left of a deconstructing assignment, as in "(a.X, b) = pair".
        private static bool IsDeconstructionTarget(ArgumentSyntax argument)
        {
            SyntaxNode node = argument.Parent;
            while (node is TupleExpressionSyntax tuple && tuple.Parent is ArgumentSyntax outer) node = outer.Parent;
            return node is TupleExpressionSyntax target && target.Parent is AssignmentExpressionSyntax assignment && assignment.Left == target;
        }

        private static bool IsUnityCallback(IMethodSymbol method)
        {
            INamedTypeSymbol type = method.ContainingType;
            if (type == null) return false;
            if (new ScriptInfo(type).IsMessage(method)) return true;
            switch (method.Name)
            {
                // Callbacks the message catalog doesn't list.
                case "ShowButton": return method.Parameters.Length == 1 && SymbolNames.IsOrDerivesFrom(type, "UnityEditor.EditorWindow");
                case "OnDidApplyAnimationProperties": return method.Parameters.IsEmpty && Frameworks.Unity.IsMonoBehaviour(type);
                case "__internalAwake": return method.Parameters.IsEmpty && Frameworks.Unity.IsScriptableObject(type);
                default: return AssetModificationCallbacks.Contains(method.Name) && SymbolNames.IsOrDerivesFrom(type, "UnityEditor.AssetModificationProcessor");
            }
        }

        // Animation clips call a component's method by the name in their event, passing the event.
        private static bool IsAnimationEventReceiver(IMethodSymbol method)
        {
            return method.Parameters.Length == 1 && SymbolNames.IsNamed(method.Parameters[0].Type, "UnityEngine.AnimationEvent")
                && Frameworks.Unity.IsMonoBehaviour(method.ContainingType);
        }

        private static bool IsCreatedByUnity(INamedTypeSymbol type)
        {
            return SymbolNames.IsOrDerivesFrom(type, "UnityEditor.AssetPostprocessor") || SymbolNames.IsOrDerivesFrom(type, "UnityEditor.AssetModificationProcessor");
        }

        // Unity finds static methods such as [SettingsProvider] and [MenuItem] ones through their
        // attribute, so the type holding them is needed even when no code names it.
        private static bool HoldsAttributedStaticMethod(INamedTypeSymbol type)
        {
            return type.GetMembers().OfType<IMethodSymbol>().Any(method => method.IsStatic && !method.GetAttributes().IsEmpty);
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
                    foreach (string name in DebuggerDisplayNames(attribute)) yield return name;
                }
            }
        }

        // [DebuggerDisplay("{Name} ({Count()})")] evaluates the expressions in braces.
        private static IEnumerable<string> DebuggerDisplayNames(AttributeData attribute)
        {
            if (!SymbolNames.IsNamed(attribute.AttributeClass, "System.Diagnostics.DebuggerDisplayAttribute")) yield break;
            IEnumerable<TypedConstant> texts = attribute.ConstructorArguments.Concat(attribute.NamedArguments.Select(argument => argument.Value));
            foreach (string text in texts.Select(constant => constant.Value).OfType<string>())
            {
                foreach (Match expression in Regex.Matches(text, @"\{([^}]*)\}"))
                {
                    foreach (Match name in DebuggerDisplayName.Matches(expression.Groups[1].Value)) yield return name.Value;
                }
            }
        }

        private static SyntaxNode FieldDeclaration(ISymbol symbol, CancellationToken ct)
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