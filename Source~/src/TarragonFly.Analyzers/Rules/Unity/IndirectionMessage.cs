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

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class IndirectionMessageAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4019";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.IndirectionMessageDiagnosticTitle,
		messageFormat: Strings.IndirectionMessageDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0019",
		description: Strings.IndirectionMessageDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeExpression), SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeExpression(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MemberAccessExpressionSyntax access)
			return;

		if (access.Name.ToFullString() != "gameObject")
			return;

		var model = context.SemanticModel;
		var symbol = model.GetSymbolInfo(access);
		var typeInfo = model.GetTypeInfo(access.Expression);

		if (symbol.Symbol is not IPropertySymbol)
			return;

		if (typeInfo.Type == null)
			return;

		if (!typeInfo.Type.Extends(typeof(UnityEngine.GameObject)))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), access.Name));
	}
}