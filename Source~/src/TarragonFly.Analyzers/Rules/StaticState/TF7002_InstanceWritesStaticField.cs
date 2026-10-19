using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7002: an instance method, property or event accessor that writes a static field, as in
    // "Instance = this;" in Awake or "spawnedCount++" in Spawn.
    //
    // Every instance shares the field, so the last one to run wins. With domain reload turned off
    // (Enter Play Mode Options) the value also survives into the next Play Mode session, so a singleton
    // or counter starts out holding the last run's object or total. Reset it in a
    // [RuntimeInitializeOnLoadMethod] method, or keep the state on the instance.
    //
    // The first write to each field in a member is reported, through the type name too, as are writes
    // in lambdas and local functions. Constructors are TF7004's job, and static members are fine. ref
    // and out arguments, element writes and static properties aren't counted. Reported from the field
    // to the operator, or on the field for ++ and --. Game code only.
    //
    // Only static fields declared in the project's own code count, so subscribing to Unity's static
    // callbacks, as in "EditorApplication.delayCall += Refresh", is fine.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InstanceWritesStaticFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7002",
            "Instance member writes a static field",
            "This instance member writes static '{0}', which every instance shares; with domain reload off it also keeps its value into the next Play Mode session",
            "StaticState",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                SyntaxNode code = Code(context.Node);
                if (code == null) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is IMethodSymbol member) || member.IsStatic) return;
                foreach ((IFieldSymbol field, Location location) in StaticFieldWrites.FirstPerField(context.SemanticModel, code, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                }
            }), SyntaxKind.MethodDeclaration, SyntaxKind.DestructorDeclaration, SyntaxKind.GetAccessorDeclaration, SyntaxKind.SetAccessorDeclaration,
                SyntaxKind.InitAccessorDeclaration, SyntaxKind.AddAccessorDeclaration, SyntaxKind.RemoveAccessorDeclaration);

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                BasePropertyDeclarationSyntax property = (BasePropertyDeclarationSyntax)context.Node;
                ArrowExpressionClauseSyntax body = property is PropertyDeclarationSyntax named ? named.ExpressionBody : ((IndexerDeclarationSyntax)property).ExpressionBody;
                if (body == null) return;
                if (!(context.SemanticModel.GetDeclaredSymbol(property, context.CancellationToken) is IPropertySymbol symbol) || symbol.IsStatic) return;
                foreach ((IFieldSymbol field, Location location) in StaticFieldWrites.FirstPerField(context.SemanticModel, body, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                }
            }), SyntaxKind.PropertyDeclaration, SyntaxKind.IndexerDeclaration);
        }

        private static SyntaxNode Code(SyntaxNode member)
        {
            switch (member)
            {
                case BaseMethodDeclarationSyntax method: return (SyntaxNode)method.Body ?? method.ExpressionBody;
                case AccessorDeclarationSyntax accessor: return (SyntaxNode)accessor.Body ?? accessor.ExpressionBody;
                default: return null;
            }
        }
    }
}