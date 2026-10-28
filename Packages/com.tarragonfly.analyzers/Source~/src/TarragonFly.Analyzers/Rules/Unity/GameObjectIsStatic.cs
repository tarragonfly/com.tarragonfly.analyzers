/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GameObjectIsStaticAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4040";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.GameObjectIsStaticDiagnosticTitle,
		messageFormat: Strings.GameObjectIsStaticDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		// Editor code may read isStatic freely.
		checks: CodeKind.Runtime | CodeKind.Tests,
		sourceRule: "UNT0040",
		description: Strings.GameObjectIsStaticDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMemberAccess), SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MemberAccessExpressionSyntax memberAccess)
			return;

		if (memberAccess.Name.Identifier.Text != "isStatic" || NameOfExpressions.IsInsideNameOf(memberAccess))
			return;

		var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
		if (typeInfo.Type == null)
			return;

		if (!typeInfo.Type.Matches(typeof(GameObject)))
			return;

		if (EditorDirectives.IsEditorOnly(memberAccess))
			return;

		if (memberAccess.IsInsideEditorOnlyMessage(context.SemanticModel))
			return;

		if (IsInsideEditorFolder(context))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
	}

	private static bool IsInsideEditorFolder(SyntaxNodeAnalysisContext context)
	{
		string path = ProjectPaths.ToProjectRelative(context.Node.SyntaxTree.FilePath);
		return path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0;
	}
}
