using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2070: a method, local function or delegate whose return type is a type named "partial",
    // written without "@". From C# 14, "partial" in that spot marks a partial method, so the code
    // changes meaning when Unity moves to a newer C# version.
    //
    // A plain "partial" return type counts, also qualified ("Game.partial") or after "ref"; a generic
    // method or local function, which C# 14 still reads as before, "partial[]", and fields,
    // properties and locals of that type are fine.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PartialReturnTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2070",
            "Return type named 'partial'",
            "From C# 14, 'partial' here marks a partial method; write '@partial' for the type",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeSyntax returnType;
                switch (context.Node)
                {
                    case MethodDeclarationSyntax method when method.TypeParameterList == null: returnType = method.ReturnType; break;
                    case LocalFunctionStatementSyntax local when local.TypeParameterList == null: returnType = local.ReturnType; break;
                    case DelegateDeclarationSyntax type: returnType = type.ReturnType; break;
                    default: return;
                }
                if (returnType is RefTypeSyntax reference) returnType = reference.Type;
                if (returnType is QualifiedNameSyntax qualified) returnType = qualified.Right;
                if (returnType is IdentifierNameSyntax name && name.Identifier.Text == "partial")
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, name.GetLocation()));
                }
            }), SyntaxKind.MethodDeclaration, SyntaxKind.LocalFunctionStatement, SyntaxKind.DelegateDeclaration);
        }
    }
}