using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2068: the name "field" used inside a property accessor. From C# 14, "field" there means the
    // property's compiler-made backing field, so code that reads or declares its own "field" changes
    // meaning when Unity moves to a newer C# version.
    //
    // Reported: "field" naming a member (a field, property or method; nameof included), and locals,
    // lambda parameters, local function parameters, foreach and out variables declared with that name.
    // Uses of such a local aren't reported again. "@field" and "this.field" are fine. Indexers and
    // event accessors aren't affected.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class FieldKeywordNameAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2068",
            "Name 'field' inside a property accessor",
            "From C# 14, 'field' here means the property's backing field; rename it or write '@field' or 'this.field'",
            "LikelyBugs",
            CodeKind.All);

        private const string Name = "field";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                PropertyDeclarationSyntax property = (PropertyDeclarationSyntax)context.Node;
                if (property.ExpressionBody != null) Check(context, property.ExpressionBody);
                if (property.AccessorList == null) return;
                foreach (AccessorDeclarationSyntax accessor in property.AccessorList.Accessors) Check(context, accessor);
            }), SyntaxKind.PropertyDeclaration);
        }

        private static void Check(SyntaxNodeAnalysisContext context, SyntaxNode body)
        {
            foreach (SyntaxToken token in body.DescendantTokens())
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken) || token.Text != Name) continue;
                if (IsDeclaration(token) || NamesMember(context, token)) context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation()));
            }
        }

        private static bool IsDeclaration(SyntaxToken token)
        {
            switch (token.Parent)
            {
                case VariableDeclaratorSyntax declarator: return declarator.Identifier == token;
                case ParameterSyntax parameter: return parameter.Identifier == token;
                case ForEachStatementSyntax forEach: return forEach.Identifier == token;
                case SingleVariableDesignationSyntax _: return true;
                case CatchDeclarationSyntax catchDeclaration: return catchDeclaration.Identifier == token;
                default: return false;
            }
        }

        private static bool NamesMember(SyntaxNodeAnalysisContext context, SyntaxToken token)
        {
            if (!(token.Parent is IdentifierNameSyntax name)) return false;
            if (name.Parent is MemberAccessExpressionSyntax access && access.Name == name) return false;
            SymbolInfo info = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken);
            ISymbol symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
            return symbol != null && !(symbol is ILocalSymbol) && !(symbol is IParameterSymbol) && !(symbol is IRangeVariableSymbol);
        }
    }
}