using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2043: a foreach whose declared type needs an explicit cast from the collection's element type,
    // as in "foreach (Goblin goblin in enemies)" over a List<Enemy>.
    //
    // Foreach casts each element silently, so the first Orc in the list throws InvalidCastException,
    // and "foreach (byte b in ints)" quietly cuts values. Filter with OfType<Goblin>() or loop over the
    // real element type.
    //
    // Collections of object, such as ArrayList, aren't reported: the cast is the only way to loop over
    // them. Reported on the declared type.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ForeachHiddenCastAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2043",
            "Foreach with a hidden cast",
            "Each element is cast from '{0}' to '{1}', which can throw or lose data; loop over '{0}' or filter with OfType<{1}>()",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                ForEachStatementSyntax loop = (ForEachStatementSyntax)context.Node;
                if (loop.Type.IsVar) return;
                ForEachStatementInfo info = context.SemanticModel.GetForEachStatementInfo(loop);
                ITypeSymbol element = info.ElementType;
                if (element == null || element.SpecialType == SpecialType.System_Object || info.ElementConversion.IsImplicit || info.ElementConversion.IsIdentity) return;
                ITypeSymbol declared = context.SemanticModel.GetTypeInfo(loop.Type, context.CancellationToken).Type;
                if (declared == null) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, loop.Type.GetLocation(),
                    element.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), declared.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }), SyntaxKind.ForEachStatement);
        }
    }
}