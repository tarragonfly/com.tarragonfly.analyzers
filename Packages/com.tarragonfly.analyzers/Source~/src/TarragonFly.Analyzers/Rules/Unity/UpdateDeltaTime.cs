/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UpdateDeltaTimeAnalyzer : ScopedAnalyzer
{
	internal const string UpdateId = "TF4004";

	internal static readonly DiagnosticDescriptor UpdateRule = Descriptors.Create(
		id: UpdateId,
		title: Strings.UpdateWithoutFixedDeltaTimeDiagnosticTitle,
		messageFormat: Strings.UpdateWithoutFixedDeltaTimeDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0004",
		description: Strings.UpdateWithoutFixedDeltaTimeDiagnosticDescription);

	internal const string FixedUpdateId = "TF4005";

	internal static readonly DiagnosticDescriptor FixedUpdateRule = Descriptors.Create(
		id: FixedUpdateId,
		title: Strings.FixedUpdateWithoutDeltaTimeDiagnosticTitle,
		messageFormat: Strings.FixedUpdateWithoutDeltaTimeDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: false, // see https://github.com/microsoft/Microsoft.Unity.Analyzers/issues/26
		checks: CodeKind.All,
		sourceRule: "UNT0005",
		description: Strings.FixedUpdateWithoutDeltaTimeDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		UpdateRule,
		FixedUpdateRule
	);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMethodDeclaration), SyntaxKind.MethodDeclaration);
	}

	private static bool IsMessage(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax methodSyntax, Type metadata, string methodName)
	{
		var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodSyntax);
		if (methodSymbol == null)
			return false;

		var scriptInfo = new ScriptInfo(methodSymbol.ContainingType);
		if (!scriptInfo.HasMessages)
			return false;

		if (!scriptInfo.IsMessage(methodSymbol))
			return false;

		return scriptInfo.Metadata == metadata && methodSymbol.Name == methodName;
	}

	private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MethodDeclarationSyntax method)
			return;

		if (IsMessage(context, method, typeof(MonoBehaviour), "Update"))
			AnalyzeMemberAccess(context, method, "Time.fixedDeltaTime", UpdateRule);

		if (IsMessage(context, method, typeof(MonoBehaviour), "FixedUpdate"))
			AnalyzeMemberAccess(context, method, "Time.deltaTime", FixedUpdateRule);
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method, string memberName, DiagnosticDescriptor descriptor)
	{
		var usages = method
			.DescendantNodes()
			.OfType<MemberAccessExpressionSyntax>()
			.Where(expression => expression.ToString() == memberName);

		foreach (var usage in usages)
			context.ReportDiagnostic(Diagnostic.Create(descriptor, usage.Name.GetLocation()));
	}
}