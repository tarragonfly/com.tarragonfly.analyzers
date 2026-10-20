/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

public class SetPositionAndRotationContext : BaseSetPositionAndRotationContext
{
	public static Lazy<BasePositionAndRotationContext> Instance => new(() => new SetPositionAndRotationContext());
	private SetPositionAndRotationContext() : base("position", "rotation", "SetPositionAndRotation") { }
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SetPositionAndRotationAnalyzer() : BasePositionAndRotationAnalyzer(SetPositionAndRotationContext.Instance.Value)
{
	private const string RuleId = "TF4022";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.SetPositionAndRotationDiagnosticTitle,
		messageFormat: Strings.SetPositionAndRotationDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0022",
		description: Strings.SetPositionAndRotationDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnPatternFound(SyntaxNodeAnalysisContext context, StatementSyntax statement)
	{
		context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetLocation()));
	}
}