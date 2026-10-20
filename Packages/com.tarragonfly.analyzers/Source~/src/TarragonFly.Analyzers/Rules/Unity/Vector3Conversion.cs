/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class Vector3ConversionAnalyzer : BaseVectorConversionAnalyzer
{
	private const string RuleId = "TF4034";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.Vector3ConversionDiagnosticTitle,
		messageFormat: Strings.Vector3ConversionDiagnosticMessageFormat,
		area: DiagnosticCategory.Readability,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0034",
		description: Strings.Vector3ConversionDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override Type FromType => typeof(Vector3);
	protected override Type ToType => typeof(Vector2);

	protected override void ReportDiagnostic(SyntaxNodeAnalysisContext context, Location location)
	{
		context.ReportDiagnostic(Diagnostic.Create(Rule, location));
	}

	protected override bool CheckArguments(IObjectCreationOperation ocOperation)
	{
		if (ocOperation.Arguments.Length != 2)
			return false;

		return base.CheckArguments(ocOperation);
	}
}