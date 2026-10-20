/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
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
public class PropertyDrawerOnGUIAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4027";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.PropertyDrawerOnGUIDiagnosticTitle,
		messageFormat: Strings.PropertyDrawerOnGUIDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0027",
		description: Strings.PropertyDrawerOnGUIDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{

		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		var name = invocation.GetMethodNameSyntax();
		if (name is not { Identifier.Text: "OnGUI" })
			return;

		var symbol = context.SemanticModel.GetSymbolInfo(invocation);

		if (symbol.Symbol is not IMethodSymbol method)
			return;

		if (method.ContainingType.ToDisplayString() != "UnityEditor.PropertyDrawer")
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), invocation));
	}
}