/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

public class SetPixelsMethodUsageAttribute : MethodUsageAttribute;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SetPixelsMethodUsageAnalyzer : MethodUsageAnalyzer<SetPixelsMethodUsageAttribute>
{
	private const string RuleId = "TF4017";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.SetPixelsMethodUsageDiagnosticTitle,
		messageFormat: Strings.SetPixelsMethodUsageDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0017",
		description: Strings.SetPixelsMethodUsageDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override IEnumerable<MethodInfo> CollectMethods()
	{
		return CollectMethods(typeof(Texture2D), typeof(Texture3D), typeof(Texture2DArray), typeof(CubemapArray));
	}
}