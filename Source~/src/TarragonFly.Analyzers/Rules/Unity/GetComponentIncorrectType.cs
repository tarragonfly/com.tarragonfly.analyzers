/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GetComponentIncorrectTypeAnalyzer : BaseGetComponentAnalyzer
{
	private const string RuleId = "TF4014";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.GetComponentIncorrectTypeDiagnosticTitle,
		messageFormat: Strings.GetComponentIncorrectTypeDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0014",
		description: Strings.GetComponentIncorrectTypeDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		var name = invocation.GetMethodNameSyntax();
		if (name == null)
			return;

		if (!KnownMethods.IsGetComponentName(name))
			return;

		var symbol = context.SemanticModel.GetSymbolInfo(invocation);

		if (symbol.Symbol is not IMethodSymbol method)
			return;

		if (!KnownMethods.IsGetComponent(method))
			return;

		if (!HasInvalidTypeArgument(method, out var identifier))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), identifier));
	}
}