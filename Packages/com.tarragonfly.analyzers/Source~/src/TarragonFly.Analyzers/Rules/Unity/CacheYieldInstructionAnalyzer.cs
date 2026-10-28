/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
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
public class CacheYieldInstructionAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4038";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.CacheYieldInstructionAnalyzerDiagnosticTitle,
		messageFormat: Strings.CacheYieldInstructionAnalyzerDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.Game,
		sourceRule: "UNT0038",
		description: Strings.CacheYieldInstructionAnalyzerDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeYieldReturn), SyntaxKind.YieldReturnStatement);
	}

	private static readonly string[] _cachableTypes =
	[
		"UnityEngine.WaitForSeconds",
		"UnityEngine.WaitForSecondsRealtime"
	];

	private static void AnalyzeYieldReturn(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not YieldStatementSyntax yieldStatement)
			return;

		if (yieldStatement.Expression is not ObjectCreationExpressionSyntax objectCreation)
			return;

		// Only a fixed literal delay is worth caching in a field.
		if (objectCreation.ArgumentList?.Arguments is not { Count: 1 } arguments)
			return;

		if (arguments[0].Expression is not LiteralExpressionSyntax)
			return;

		var typeSymbol = context.SemanticModel.GetTypeInfo(objectCreation).Type;
		if (typeSymbol == null)
			return;

		var typeName = typeSymbol.ToDisplayString();
		if (!_cachableTypes.Contains(typeName))
			return;

		context.ReportDiagnostic(Diagnostic.Create(
			Rule,
			objectCreation.GetLocation(),
			typeSymbol.Name
		));
	}
}