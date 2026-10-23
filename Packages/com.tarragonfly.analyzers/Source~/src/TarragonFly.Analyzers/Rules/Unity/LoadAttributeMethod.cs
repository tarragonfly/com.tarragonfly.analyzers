/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LoadAttributeMethodAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4015";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.LoadAttributeMethodDiagnosticTitle,
		messageFormat: Strings.LoadAttributeMethodDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0015",
		description: Strings.LoadAttributeMethodDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMethodDeclaration), SyntaxKind.MethodDeclaration);
	}

	internal static bool MethodMatches(SyntaxNode? node, SemanticModel model, [NotNullWhen(true)] out MethodDeclarationSyntax? syntax, [NotNullWhen(true)] out IMethodSymbol? symbol)
	{
		syntax = null;
		symbol = null;

		if (node is not MethodDeclarationSyntax methodSyntax)
			return false;

		syntax = methodSyntax;

		if (model.GetDeclaredSymbol(methodSyntax) is not { } methodSymbol)
			return false;

		if (!IsDecorated(methodSymbol))
			return false;

		symbol = methodSymbol;
		return true;
	}

	internal static bool IsDecorated(IMethodSymbol symbol, bool onlyEditorAttributes = false)
	{
		return symbol
			.GetAttributes()
			.Any(a => a.AttributeClass != null && IsLoadAttributeType(a.AttributeClass, onlyEditorAttributes));
	}

	private static bool IsLoadAttributeType(ITypeSymbol type, bool onlyEditorAttributes)
	{
		return type.Matches(typeof(UnityEditor.InitializeOnLoadMethodAttribute))
			   || type.Matches(typeof(UnityEditor.Callbacks.DidReloadScripts))
			   || (type.Matches(typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute)) && !onlyEditorAttributes);
	}


	private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (!MethodMatches(context.Node, context.SemanticModel, out var syntax, out var symbol))
			return;

		if (symbol is { IsStatic: true, Parameters.Length: 0 })
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, syntax.Identifier.GetLocation(), symbol.Name));
	}
}