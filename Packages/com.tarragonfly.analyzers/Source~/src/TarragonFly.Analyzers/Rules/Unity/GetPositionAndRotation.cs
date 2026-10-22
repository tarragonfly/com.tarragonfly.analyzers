/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

public class GetPositionAndRotationContext : BaseGetPositionAndRotationContext
{
	public static Lazy<BasePositionAndRotationContext> Instance => new(() => new GetPositionAndRotationContext());
	private GetPositionAndRotationContext() : base("position", "rotation", "GetPositionAndRotation") { }
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GetPositionAndRotationAnalyzer() : BasePositionAndRotationAnalyzer(GetPositionAndRotationContext.Instance.Value)
{
	private const string RuleId = "TF4036";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.GetPositionAndRotationDiagnosticTitle,
		messageFormat: Strings.GetPositionAndRotationDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.Game,
		sourceRule: "UNT0036",
		description: Strings.GetPositionAndRotationDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeExpression), SyntaxKind.SimpleAssignmentExpression);
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeExpression), SyntaxKind.VariableDeclaration);
	}

	protected override void OnPatternFound(SyntaxNodeAnalysisContext context, StatementSyntax statement)
	{
		context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetLocation()));
	}
}