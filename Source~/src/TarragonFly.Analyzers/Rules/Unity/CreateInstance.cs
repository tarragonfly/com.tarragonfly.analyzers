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

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class CreateInstanceAnalyzer : ScopedAnalyzer
{
	public const string ComponentId = "TF4010";

	internal static readonly DiagnosticDescriptor ComponentIdRule = Descriptors.Create(
		id: ComponentId,
		title: Strings.CreateComponentInstanceDiagnosticTitle,
		messageFormat: Strings.CreateComponentInstanceDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0010",
		description: Strings.CreateMonoBehaviourInstanceDiagnosticDescription);

	public const string ScriptableObjectId = "TF4011";

	internal static readonly DiagnosticDescriptor ScriptableObjectRule = Descriptors.Create(
		id: ScriptableObjectId,
		title: Strings.CreateScriptableObjectInstanceDiagnosticTitle,
		messageFormat: Strings.CreateScriptableObjectInstanceDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0011",
		description: Strings.CreateScriptableObjectInstanceDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ComponentIdRule, ScriptableObjectRule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeObjectCreation), SyntaxKind.ObjectCreationExpression);
	}

	private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ObjectCreationExpressionSyntax creation)
			return;

		var typeInfo = context.SemanticModel.GetTypeInfo(creation);
		if (typeInfo.Type == null)
			return;

		if (typeInfo.Type.Extends(typeof(UnityEngine.ScriptableObject)))
		{
			context.ReportDiagnostic(Diagnostic.Create(ScriptableObjectRule, creation.GetLocation(), typeInfo.Type.Name));
			return;
		}

		if (!typeInfo.Type.Extends(typeof(UnityEngine.Component)))
			return;

		context.ReportDiagnostic(Diagnostic.Create(ComponentIdRule, creation.GetLocation(), typeInfo.Type.Name));
	}
}