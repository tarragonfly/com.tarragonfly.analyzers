using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Settings;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1029: a direct call to a member the project routes through its own helper, as in
    // "Root.Q<Button>("play")" in a view when every view is meant to write Element<Button>("play").
    //
    // The helper is where the project keeps one decision (the missing-element warning, the log
    // prefix, the null check), and a direct call quietly skips it. The rule only knows the members
    // the settings file lists, so it's off until discouraged_members names some.
    //
    // An entry is "Namespace.Type.Member -> Replacement", one per comma, and matches every overload
    // of that member, extension and static forms included. A parameter list after the member,
    // "(VisualElement, string, string)", narrows it to one overload; the types can be written short
    // or with their namespaces, and ref and out modifiers are left out. Calls inside a type that
    // discouraged_members_allowed_in lists, such as the helper's own class, are left alone, nested
    // types included; an entry there is the type's name with or without its namespace. Only method
    // calls are checked, not property or field reads. Reported on the called member's name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DiscouragedMemberCallAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1029",
            "Direct call where the project has a helper",
            "Call {1} instead of {0}; the project keeps that decision in one place",
            "Readability",
            CodeKind.All,
            description: "Off until discouraged_members lists members in the settings file. discouraged_members_allowed_in names the types that may still call them directly.");

        private static readonly SymbolDisplayFormat ShortTypeNames = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        private static readonly SymbolDisplayFormat FullTypeNames = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            ImmutableArray<DiscouragedMember> discouraged = scope.Settings.DiscouragedMembers;
            if (discouraged.IsEmpty) return;
            ImmutableArray<string> allowedIn = scope.Settings.DiscouragedMembersAllowedIn;

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
                if (!(context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is IMethodSymbol method)) return;
                IMethodSymbol original = (method.ReducedFrom ?? method).OriginalDefinition;
                DiscouragedMember entry = Match(discouraged, original);
                if (entry == null || IsAllowedHere(context, invocation, allowedIn)) return;

                Location location = (invocation.Expression as MemberAccessExpressionSyntax)?.Name.Identifier.GetLocation() ?? invocation.Expression.GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, original.ContainingType.Name + "." + original.Name, entry.Replacement));
            }), SyntaxKind.InvocationExpression);
        }

        private static DiscouragedMember Match(ImmutableArray<DiscouragedMember> discouraged, IMethodSymbol method)
        {
            string typeName = DiscouragedMember.StripTypeArguments(method.ContainingType.ToDisplayString());
            foreach (DiscouragedMember entry in discouraged)
            {
                if (entry.MemberName != method.Name || entry.TypeName != typeName) continue;
                if (entry.ParameterTypes == null || entry.ParameterTypes == ParameterTypes(method, ShortTypeNames) || entry.ParameterTypes == ParameterTypes(method, FullTypeNames)) return entry;
            }
            return null;
        }

        // "VisualElement, string, string": the overload's parameter types, the extension method's
        // receiver included.
        private static string ParameterTypes(IMethodSymbol method, SymbolDisplayFormat format)
        {
            string[] types = new string[method.Parameters.Length];
            for (int i = 0; i < types.Length; i++) types[i] = method.Parameters[i].Type.ToDisplayString(format);
            return string.Join(", ", types);
        }

        private static bool IsAllowedHere(SyntaxNodeAnalysisContext context, SyntaxNode node, ImmutableArray<string> allowedIn)
        {
            if (allowedIn.IsEmpty) return false;
            ISymbol enclosing = context.SemanticModel.GetEnclosingSymbol(node.SpanStart, context.CancellationToken);
            for (INamedTypeSymbol type = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType; type != null; type = type.ContainingType)
            {
                string fullName = DiscouragedMember.StripTypeArguments(type.OriginalDefinition.ToDisplayString());
                foreach (string allowed in allowedIn)
                {
                    string name = DiscouragedMember.StripTypeArguments(allowed);
                    if (name == fullName || name == type.Name) return true;
                }
            }
            return false;
        }
    }
}
