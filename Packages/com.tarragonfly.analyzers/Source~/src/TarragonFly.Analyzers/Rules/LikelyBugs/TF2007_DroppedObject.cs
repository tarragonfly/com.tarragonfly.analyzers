using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2007: an object created as a statement on its own, "new Spawner();", and then dropped.
    //
    // Either the result was meant to be kept, or the constructor does work that belongs in a method.
    //
    // "_ = new X();" says the drop is deliberate and is fine. Reported on the creation. Game code only.
    //
    // UnityEngine.Object types are skipped, since "new GameObject(name)" adds the object to the open
    // scene and is a normal way to create one.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DroppedObjectAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2007",
            "Object created and dropped",
            "This creates a '{0}' and drops it; keep the result or move the constructor's work into a method",
            "LikelyBugs",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ExpressionSyntax expression = ((ExpressionStatementSyntax)context.Node).Expression;
                if (!(expression is BaseObjectCreationExpressionSyntax)) return;
                ITypeSymbol type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
                if (type == null || Frameworks.Unity.IsUnityObject(type)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, expression.GetLocation(), type.Name));
            }), SyntaxKind.ExpressionStatement);
        }
    }
}