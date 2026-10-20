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

public class SetLocalPositionAndRotationContext : BaseSetPositionAndRotationContext
{
	public static Lazy<BasePositionAndRotationContext> Instance => new(() => new SetLocalPositionAndRotationContext());
	private SetLocalPositionAndRotationContext() : base("localPosition", "localRotation", "SetLocalPositionAndRotation") { }
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SetLocalPositionAndRotationAnalyzer() : BasePositionAndRotationAnalyzer(SetLocalPositionAndRotationContext.Instance.Value)
{
	private const string RuleId = "TF4032";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.SetLocalPositionAndRotationDiagnosticTitle,
		messageFormat: Strings.SetLocalPositionAndRotationDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0032",
		description: Strings.SetLocalPositionAndRotationDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnPatternFound(SyntaxNodeAnalysisContext context, StatementSyntax statement)
	{
		context.ReportDiagnostic(Diagnostic.Create(Rule, statement.GetLocation()));
	}
}