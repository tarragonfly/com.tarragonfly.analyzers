/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

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
		checks: CodeKind.All,
		sourceRule: "UNT0040",
		description: Strings.GameObjectIsStaticDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	private static readonly Regex EditorFolderRegex = new(@"[/\\]Assets[/\\].*[/\\]Editor[/\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMemberAccess), SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MemberAccessExpressionSyntax memberAccess)
			return;

		if (memberAccess.Name.Identifier.Text != "isStatic")
			return;

		var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression);
		if (typeInfo.Type == null)
			return;

		if (!typeInfo.Type.Matches(typeof(UnityEngine.GameObject)))
			return;

		if (DirectiveHelper.IsInsideDirective(memberAccess, "UNITY_EDITOR"))
			return;

		if (memberAccess.IsInsideEditorOnlyMessage(context.SemanticModel))
			return;

		if (IsInsideEditorFolder(context))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
	}

	private static bool IsInsideEditorFolder(SyntaxNodeAnalysisContext context)
	{
		var filePath = context.Node.SyntaxTree.FilePath;
		return !string.IsNullOrWhiteSpace(filePath) && EditorFolderRegex.IsMatch(filePath);
	}
}