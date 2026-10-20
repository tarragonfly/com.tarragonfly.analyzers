using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.StaticState
{
    // TF7004: an instance constructor that writes a static field, as in "public Enemy() { aliveCount++;
    // }".
    //
    // The field changes every time an object is created, which is rarely what a static is for, and
    // with domain reload turned off (Enter Play Mode Options) the value also survives into the next
    // Play Mode session. Set static state in a static constructor or a
    // [RuntimeInitializeOnLoadMethod] method instead.
    //
    // Same counting as TF7002: the first write to each field per constructor, lambdas included. Static
    // constructors are fine. Reported from the field to the operator, or on the field for ++ and --.
    // Game code only.
    //
    // As in TF7002, only static fields declared in scripts count: this assembly's, or another
    // assembly's built from the project's or a package's scripts.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ConstructorWritesStaticFieldAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF7004",
            "Static field written in an instance constructor",
            "This constructor writes static '{0}' every time an object is created; with domain reload off it also keeps its value into the next Play Mode session",
            "StaticState",
            CodeKind.Game);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ConstructorDeclarationSyntax constructor = (ConstructorDeclarationSyntax)context.Node;
                if (constructor.Modifiers.Any(SyntaxKind.StaticKeyword)) return;
                SyntaxNode code = (SyntaxNode)constructor.Body ?? constructor.ExpressionBody;
                if (code == null) return;
                foreach ((IFieldSymbol field, Location location) in StaticFieldWrites.FirstPerField(context.SemanticModel, code, context.CancellationToken))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, location, field.Name));
                }
            }), SyntaxKind.ConstructorDeclaration);
        }
    }
}