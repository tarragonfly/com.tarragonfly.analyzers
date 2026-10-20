/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
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
public class TagComparisonAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4002";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.TagComparisonDiagnosticTitle,
		messageFormat: Strings.TagComparisonDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0002",
		description: Strings.TagComparisonDiagnosticDescription);

	public sealed override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected sealed override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeBinaryExpression), SyntaxKind.EqualsExpression, SyntaxKind.NotEqualsExpression);
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocationExpression), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocationExpression(SyntaxNodeAnalysisContext context)
	{
		var expr = (InvocationExpressionSyntax)context.Node;
		var nameSyntax = expr.GetMethodNameSyntax();

		if (!IsSupportedMethod(context, nameSyntax))
			return;

		if (expr.Expression is MemberAccessExpressionSyntax mae)
		{
			if (IsReportableExpression(context, mae.Expression))
				context.ReportDiagnostic(Diagnostic.Create(Rule, expr.GetLocation()));
		}

		foreach (var argument in expr.ArgumentList.Arguments)
		{
			if (IsReportableExpression(context, argument.Expression))
				context.ReportDiagnostic(Diagnostic.Create(Rule, expr.GetLocation()));
		}
	}

	private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not BinaryExpressionSyntax expr)
			return;

		if (IsReportableExpression(context, expr.Left)
			|| IsReportableExpression(context, expr.Right))
			context.ReportDiagnostic(Diagnostic.Create(Rule, expr.GetLocation()));
	}

	private static bool IsSupportedMethod(SyntaxNodeAnalysisContext context, SimpleNameSyntax? nameSyntax)
	{
		if (nameSyntax == null)
			return false;

		const string equalsName = "Equals";

		var name = GetMethodName(nameSyntax);
		if (name != equalsName)
			return false;

		var symbolInfo = context.SemanticModel.GetSymbolInfo(nameSyntax);
		if (symbolInfo.Symbol is not IMethodSymbol symbol)
			return false;

		if (symbol.Name != equalsName || symbol.Parameters.Length > 2)
			return false;

		if (!symbol.ReturnType.Matches(typeof(bool)))
			return false;

		var containgType = symbol.ContainingType;
		return containgType.Matches(typeof(string)) || containgType.Matches(typeof(object));
	}

	private static string GetMethodName(SimpleNameSyntax nameSyntax) => nameSyntax.Identifier.Text;

	private static bool IsReportableExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expr)
	{
		return IsReportableExpression(context.SemanticModel, expr);
	}

	internal static bool IsReportableExpression(SemanticModel model, ExpressionSyntax expr)
	{
		var symbol = model.GetSymbolInfo(expr).Symbol;
		if (symbol == null)
			return false;

		return IsReportableSymbol(symbol);
	}

	private static bool IsReportableSymbol(ISymbol symbol)
	{
		if (symbol is not IPropertySymbol propertySymbol)
			return false;

		var containingType = propertySymbol.ContainingType;
		return propertySymbol.Name == nameof(UnityEngine.Component.tag)
			   && (containingType.Matches(typeof(UnityEngine.GameObject)) || containingType.Matches(typeof(UnityEngine.Component)));
	}
}