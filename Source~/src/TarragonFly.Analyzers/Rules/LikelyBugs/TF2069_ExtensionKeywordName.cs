using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2069: a type or type parameter named "extension", or a member whose type starts with one. From
    // C# 14, a member starting with "extension" opens an extension block, so this code stops compiling
    // when Unity moves to a newer C# version.
    //
    // Reported: class, struct, interface, enum, record and delegate declarations named "extension",
    // type parameters with that name, and fields, properties, indexers, methods and operators whose
    // declared type starts with it ("extension", "extension[]", "extension?"). Parameter, local,
    // generic argument and conversion types are fine, as is "@extension". Game code only.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ExtensionKeywordNameAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2069",
            "Type named 'extension'",
            "From C# 14, 'extension' here starts an extension block; rename the type or write '@extension'",
            "LikelyBugs",
            CodeKind.Game);

        private const string Name = "extension";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxToken token;
                switch (context.Node)
                {
                    case BaseTypeDeclarationSyntax type: token = type.Identifier; break;
                    case DelegateDeclarationSyntax type: token = type.Identifier; break;
                    case TypeParameterSyntax parameter: token = parameter.Identifier; break;
                    case FieldDeclarationSyntax field: token = field.Declaration.Type.GetFirstToken(); break;
                    case BasePropertyDeclarationSyntax property when !(property is EventDeclarationSyntax): token = property.Type.GetFirstToken(); break;
                    case MethodDeclarationSyntax method: token = method.ReturnType.GetFirstToken(); break;
                    case OperatorDeclarationSyntax op: token = op.ReturnType.GetFirstToken(); break;
                    default: return;
                }
                if (token.IsKind(SyntaxKind.IdentifierToken) && token.Text == Name) context.ReportDiagnostic(Diagnostic.Create(Rule, token.GetLocation()));
            }), SyntaxKind.ClassDeclaration, SyntaxKind.StructDeclaration, SyntaxKind.InterfaceDeclaration, SyntaxKind.EnumDeclaration,
                SyntaxKind.RecordDeclaration, SyntaxKind.DelegateDeclaration, SyntaxKind.TypeParameter, SyntaxKind.FieldDeclaration,
                SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration, SyntaxKind.MethodDeclaration, SyntaxKind.OperatorDeclaration);
        }
    }
}