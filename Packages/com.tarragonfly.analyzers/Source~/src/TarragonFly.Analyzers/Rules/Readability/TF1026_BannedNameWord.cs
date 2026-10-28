using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using TarragonFly.Analyzers.Frameworks;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1026: a declared name built from a word the project has decided not to use, as in
    // "class WidgetWorkspace" or "int widgetCount" in a project whose settings list "widget".
    //
    // Some words say nothing about what the code does, and every file that picks one up spreads it
    // further. The project lists them once in the settings file and the rule points at each new use,
    // so the name gets replaced with what the thing actually holds or does.
    //
    // Checked: the names of types, methods, local functions, properties, events, fields, enum
    // members, parameters, locals, pattern and out variables, type parameters and namespace parts,
    // plus the file name. A name is split into lowercase tokens at underscores and case changes
    // ("fluxCapacitorId", "FluxCapacitorId" and "flux_capacitor_id" all give flux, capacitor, id; a run of
    // capitals stays one token, so "HTTPServer" is http, server; digits stay with the letters before
    // them, so "Vector2Conversion" is vector2, conversion). An entry in banned_name_words matches
    // when its own tokens appear in order among the name's tokens, so "flux capacitor" matches
    // fluxCapacitor and FluxCapacitorId but not "capacitorFlux", and "widget" doesn't match
    // "widgets". An entry in
    // banned_method_prefixes matches a method or local function whose first tokens are the entry's.
    //
    // Not reported: uses of a name (a call, an attribute, a type reference), overrides and explicit
    // interface implementations, which take their name from the base, constructors, which take the
    // type's, and, while banned_name_words_skip_serialized is true, serialized fields: those with
    // [SerializeField] or [SerializeReference], public fields on a UnityEngine.Object subclass or a
    // [Serializable] type, and properties with [field: SerializeField]. Renaming those loses the
    // values stored in scenes, prefabs and assets, so they're left for a migration. Without words in
    // the settings file the rule has nothing to report. Reported on the identifier; the file name is
    // reported at the start of the file.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BannedNameWordAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1026",
            "Name uses a word the project avoids",
            "'{0}' uses '{1}', a word this project avoids; name what it actually does",
            "Readability",
            CodeKind.All,
            description: "The words come from banned_name_words and banned_method_prefixes in the settings file; without them the rule has nothing to report. " +
                "While banned_name_words_skip_serialized is true, fields Unity saves ([SerializeField], or public on a UnityEngine.Object subclass or a [Serializable] type) " +
                "are skipped, because renaming them loses the values stored in scenes, prefabs and assets.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        private enum Declaration { None, Other, Method, Field, Property }

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            List<BannedEntry> words = BannedEntry.Parse(scope.Settings.BannedNameWords);
            List<BannedEntry> prefixes = BannedEntry.Parse(scope.Settings.BannedMethodPrefixes);
            if (words.Count == 0 && prefixes.Count == 0) return;
            bool skipSerialized = scope.Settings.BannedNameWordsSkipSerialized;

            start.RegisterSemanticModelAction(context =>
            {
                SyntaxTree tree = context.SemanticModel.SyntaxTree;
                if (!scope.IsAnalyzed(tree)) return;

                string fileName = Path.GetFileName(tree.FilePath);
                string fileWord = FirstMatch(Tokens(Path.GetFileNameWithoutExtension(tree.FilePath)), words, false);
                if (fileWord != null) context.ReportDiagnostic(Diagnostic.Create(Rule, Location.Create(tree, new TextSpan(0, 0)), fileName, fileWord));

                foreach (SyntaxToken identifier in tree.GetRoot(context.CancellationToken).DescendantTokens())
                {
                    if (!identifier.IsKind(SyntaxKind.IdentifierToken)) continue;
                    Declaration declaration = Classify(identifier);
                    if (declaration == Declaration.None) continue;
                    if (skipSerialized && IsSerializedByUnity(declaration, identifier.Parent, context.SemanticModel, context.CancellationToken)) continue;

                    List<string> tokens = Tokens(identifier.ValueText);
                    string word = FirstMatch(tokens, words, false) ?? (declaration == Declaration.Method ? FirstMatch(tokens, prefixes, true) : null);
                    if (word != null) context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation(), identifier.ValueText, word));
                }
            });
        }

        // Whether the identifier declares something, and which kind when the kind matters later.
        private static Declaration Classify(SyntaxToken identifier)
        {
            switch (identifier.Parent)
            {
                case BaseTypeDeclarationSyntax type:
                    return type.Identifier == identifier ? Declaration.Other : Declaration.None;
                case DelegateDeclarationSyntax delegateDeclaration:
                    return delegateDeclaration.Identifier == identifier ? Declaration.Other : Declaration.None;
                case MethodDeclarationSyntax method:
                    return method.Identifier == identifier && NamesItself(method.ExplicitInterfaceSpecifier, method.Modifiers) ? Declaration.Method : Declaration.None;
                case LocalFunctionStatementSyntax localFunction:
                    return localFunction.Identifier == identifier ? Declaration.Method : Declaration.None;
                case PropertyDeclarationSyntax property:
                    return property.Identifier == identifier && NamesItself(property.ExplicitInterfaceSpecifier, property.Modifiers) ? Declaration.Property : Declaration.None;
                case EventDeclarationSyntax eventDeclaration:
                    return eventDeclaration.Identifier == identifier && NamesItself(eventDeclaration.ExplicitInterfaceSpecifier, eventDeclaration.Modifiers) ? Declaration.Other : Declaration.None;
                case VariableDeclaratorSyntax variable:
                    if (variable.Identifier != identifier) return Declaration.None;
                    return variable.Parent?.Parent is FieldDeclarationSyntax ? Declaration.Field : Declaration.Other;
                case EnumMemberDeclarationSyntax member:
                    return member.Identifier == identifier ? Declaration.Other : Declaration.None;
                case ParameterSyntax parameter:
                    return parameter.Identifier == identifier ? Declaration.Other : Declaration.None;
                case TypeParameterSyntax typeParameter:
                    return typeParameter.Identifier == identifier ? Declaration.Other : Declaration.None;
                case ForEachStatementSyntax loop:
                    return loop.Identifier == identifier ? Declaration.Other : Declaration.None;
                case SingleVariableDesignationSyntax designation:
                    return designation.Identifier == identifier ? Declaration.Other : Declaration.None;
                case CatchDeclarationSyntax catchDeclaration:
                    return catchDeclaration.Identifier == identifier ? Declaration.Other : Declaration.None;
                case FromClauseSyntax fromClause:
                    return fromClause.Identifier == identifier ? Declaration.Other : Declaration.None;
                case LetClauseSyntax letClause:
                    return letClause.Identifier == identifier ? Declaration.Other : Declaration.None;
                case JoinClauseSyntax joinClause:
                    return joinClause.Identifier == identifier ? Declaration.Other : Declaration.None;
                case JoinIntoClauseSyntax joinInto:
                    return joinInto.Identifier == identifier ? Declaration.Other : Declaration.None;
                case QueryContinuationSyntax continuation:
                    return continuation.Identifier == identifier ? Declaration.Other : Declaration.None;
                case IdentifierNameSyntax name:
                    return IsNamespacePart(name) ? Declaration.Other : Declaration.None;
                default:
                    return Declaration.None;
            }
        }

        // An override or an explicit interface implementation takes its name from the base, so the
        // base declaration is where the rename happens.
        private static bool NamesItself(ExplicitInterfaceSpecifierSyntax explicitInterface, SyntaxTokenList modifiers)
        {
            return explicitInterface == null && !modifiers.Any(SyntaxKind.OverrideKeyword);
        }

        // "Game" and "Widgets" in "namespace Game.Widgets", not the "Game" in "using Game.Widgets;".
        private static bool IsNamespacePart(IdentifierNameSyntax name)
        {
            SyntaxNode outermost = name;
            while (outermost.Parent is QualifiedNameSyntax qualified) outermost = qualified;
            return outermost.Parent is BaseNamespaceDeclarationSyntax declaration && declaration.Name == outermost;
        }

        // A field Unity writes into scenes, prefabs and assets, or a property whose backing field it
        // writes through [field: SerializeField]. The name is part of the saved data.
        private static bool IsSerializedByUnity(Declaration declaration, SyntaxNode node, SemanticModel model, CancellationToken ct)
        {
            switch (declaration)
            {
                case Declaration.Field:
                    return IsSerializedByUnity(model.GetDeclaredSymbol(node, ct) as IFieldSymbol);
                case Declaration.Property:
                    IPropertySymbol property = model.GetDeclaredSymbol(node, ct) as IPropertySymbol;
                    if (property == null) return false;
                    IFieldSymbol backingField = property.ContainingType.GetMembers().OfType<IFieldSymbol>()
                        .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
                    return IsSerializedByUnity(backingField);
                default:
                    return false;
            }
        }

        private static bool IsSerializedByUnity(IFieldSymbol field)
        {
            if (field == null || !Frameworks.Unity.IsSerializedByUnity(field)) return false;
            if (SymbolNames.HasAttribute(field, "UnityEngine.SerializeField") || SymbolNames.HasAttribute(field, "UnityEngine.SerializeReference")) return true;
            return Frameworks.Unity.IsUnityObject(field.ContainingType) || SymbolNames.HasAttribute(field.ContainingType, "System.SerializableAttribute");
        }

        // The text of the first entry found in the name, or null. With prefixOnly the entry has to
        // start the name.
        private static string FirstMatch(List<string> nameTokens, List<BannedEntry> entries, bool prefixOnly)
        {
            foreach (BannedEntry entry in entries)
            {
                if (entry.Tokens.Count > nameTokens.Count) continue;
                int lastStart = prefixOnly ? 0 : nameTokens.Count - entry.Tokens.Count;
                for (int at = 0; at <= lastStart; at++)
                {
                    if (MatchesAt(nameTokens, at, entry.Tokens)) return entry.Text;
                }
            }
            return null;
        }

        private static bool MatchesAt(List<string> nameTokens, int at, List<string> entryTokens)
        {
            for (int i = 0; i < entryTokens.Count; i++)
            {
                if (nameTokens[at + i] != entryTokens[i]) return false;
            }
            return true;
        }

        // The lowercase words of a name: "fluxCapacitorId", "FluxCapacitorId", "flux_capacitor_id"
        // and "flux capacitor id" all give flux, capacitor, id.
        private static List<string> Tokens(string name)
        {
            List<string> tokens = new List<string>();
            int start = 0;
            for (int i = 0; i < name.Length; i++)
            {
                if (IsSeparator(name[i]))
                {
                    AddToken(name, start, i, tokens);
                    start = i + 1;
                }
                else if (StartsWord(name, i))
                {
                    AddToken(name, start, i, tokens);
                    start = i;
                }
            }
            AddToken(name, start, name.Length, tokens);
            return tokens;
        }

        private static bool IsSeparator(char character)
        {
            return character == '_' || character == '-' || char.IsWhiteSpace(character);
        }

        // A capital after a lowercase letter or digit starts a word, and so does the capital that
        // ends a run of capitals before a lowercase letter: the S of "HTTPServer".
        private static bool StartsWord(string name, int i)
        {
            if (i == 0 || !char.IsUpper(name[i])) return false;
            char before = name[i - 1];
            if (char.IsLower(before) || char.IsDigit(before)) return true;
            return char.IsUpper(before) && i + 1 < name.Length && char.IsLower(name[i + 1]);
        }

        private static void AddToken(string name, int start, int end, List<string> tokens)
        {
            if (end > start) tokens.Add(name.Substring(start, end - start).ToLowerInvariant());
        }

        // One entry from the settings list, as written and as the tokens it has to match.
        private sealed class BannedEntry
        {
            private BannedEntry(string text, List<string> tokens)
            {
                Text = text;
                Tokens = tokens;
            }

            public string Text { get; }
            public List<string> Tokens { get; }

            public static List<BannedEntry> Parse(ImmutableArray<string> entries)
            {
                List<BannedEntry> parsed = new List<BannedEntry>();
                foreach (string entry in entries)
                {
                    List<string> tokens = BannedNameWordAnalyzer.Tokens(entry);
                    if (tokens.Count > 0) parsed.Add(new BannedEntry(entry, tokens));
                }
                return parsed;
            }
        }
    }
}
