/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using Unity.Mathematics;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class VectorMathAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4024";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.VectorMathDiagnosticTitle,
		messageFormat: Strings.VectorMathDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.Game,
		sourceRule: "UNT0024",
		description: Strings.VectorMathDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	internal Type[] SupportedTypes =
	[
		typeof(Vector2),
		typeof(Vector3),
		typeof(Vector4),
		typeof(float2),
		typeof(float3),
		typeof(float4),
	];

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{

		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMultiplyExpression), SyntaxKind.MultiplyExpression);
	}

	private void AnalyzeMultiplyExpression(SyntaxNodeAnalysisContext context)
	{
		var expr = context.Node;

		foreach (var type in SupportedTypes)
		{
			// Only keep the top-level expression, so make sure we don't have a reportable parent
			if (!IsReportableExpression(context, expr, type) || IsReportableExpression(context, expr.Parent, type))
				continue;

			context.ReportDiagnostic(Diagnostic.Create(Rule, expr.GetLocation()));
		}
	}

	private static bool IsReportableExpression(SyntaxNodeAnalysisContext context, SyntaxNode? node, Type vectorType)
	{
		if (node == null)
			return false;

		return IsSupportedExpression(context, node, vectorType)
			   && NeedsOrdering(context, (ExpressionSyntax)node);
	}

	private static bool NeedsOrdering(SyntaxNodeAnalysisContext context, ExpressionSyntax node)
	{
		var operands = GetOperands(context.SemanticModel, node)
			.ToArray();

		if (operands.Length < 3)
			return false;

		int firstVectorIndex = -1;
		int lastScalarIndex = -1;
		int scalarCount = 0;
		for (int i = 0; i < operands.Length; i++)
		{
			var (_, typeInfo) = operands[i];
			if (IsFloatType(typeInfo))
			{
				lastScalarIndex = i;
				scalarCount++;
			}
			else
			{
				if (firstVectorIndex == -1)
					firstVectorIndex = i;
			}
		}

		return firstVectorIndex != -1
			   && lastScalarIndex != -1
			   && firstVectorIndex < lastScalarIndex
			   && scalarCount >= 2;
	}

	internal static bool IsFloatType(TypeInfo typeInfo)
	{
		return typeInfo.ConvertedType != null && typeInfo.ConvertedType.Matches(typeof(float));
	}

	internal static IEnumerable<(ExpressionSyntax, TypeInfo)> GetOperands(SemanticModel model, ExpressionSyntax node)
	{
		if (node is BinaryExpressionSyntax be)
		{
			foreach (var o in GetOperands(model, be.Left))
				yield return o;

			foreach (var o in GetOperands(model, be.Right))
				yield return o;
		}
		else
		{
			yield return new(node, model.GetTypeInfo(node));
		}
	}

	private static bool IsSupportedExpression(SyntaxNodeAnalysisContext context, SyntaxNode node, Type vectorType)
	{
		if (node is not BinaryExpressionSyntax be)
		{
			var model = context.SemanticModel;
			var typeInfo = model.GetTypeInfo(node);

			// We want to check the converted type here, given all is float-based.
			if (IsFloatType(typeInfo))
				return true;

			// Else we want a matrix-like item
			var type = typeInfo.Type;
			return type != null && type.Matches(vectorType);
		}

		if (!node.IsKind(SyntaxKind.MultiplyExpression))
			return false;

		return IsSupportedExpression(context, be.Left, vectorType) && IsSupportedExpression(context, be.Right, vectorType);
	}
}