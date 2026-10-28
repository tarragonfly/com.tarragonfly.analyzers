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
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class RequireComponentAnalyzer : BaseGetComponentAnalyzer
{
	private const string RuleId = "TF4039";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.RequireComponentAnalyzerDiagnosticTitle,
		messageFormat: Strings.RequireComponentAnalyzerDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0039",
		description: Strings.RequireComponentAnalyzerDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		var model = context.SemanticModel;
		if (!IsGenericGetComponent(invocation, model, out var method))
			return;

		if (model.GetOperation(invocation) is not IInvocationOperation invocationOperation)
			return;

		if (invocationOperation.Instance is not IInstanceReferenceOperation referenceOperation)
			return;

		var containerType = referenceOperation.Type;
		if (containerType == null)
			return;

		if (!containerType.Extends(typeof(MonoBehaviour)))
			return;

		// GetComponent inside editor-only messages is typically used to populate
		// serialized fields with overridable defaults, so the component is not a hard requirement
		if (invocation.IsInsideEditorOnlyMessage(model))
			return;

		var componentType = method.TypeArguments.First(); // Checked by IsGenericGetComponent
		if (componentType.TypeKind == TypeKind.TypeParameter)
			return;

		if (IsTypeAlreadyRequired(containerType, componentType))
			return;

		if (IsInvocationNullChecked(invocation))
			return;

		var invocationParent = invocation.Parent;
		if (invocationParent == null)
			return;

		if (TryGetTargetIdentifier(invocationParent, out var targetIdentifier)
			&& TryGetNextTopNode(invocationParent, out var ifNode)
			&& TryGetConditionIdentifier(ifNode, out var conditionIdentifier, out _, out _)
			&& conditionIdentifier.Value.Text != targetIdentifier.Value.Text)
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	// [RequireComponent] is inherited, so a base class's attribute counts too. Upstream only read the
	// class's own attributes and reported components a base class already requires.
	private static bool IsTypeAlreadyRequired(ITypeSymbol containerType, ITypeSymbol componentType)
	{
		for (var type = containerType; type != null; type = type.BaseType)
		{
			if (IsTypeRequiredBy(type, componentType))
				return true;
		}

		return false;
	}

	private static bool IsTypeRequiredBy(ITypeSymbol containerType, ITypeSymbol componentType)
	{
		var typeSymbols = containerType
			.GetAttributes()
			.Where(a => a.AttributeClass != null && a.AttributeClass.Matches(typeof(RequireComponent)))
			.SelectMany(a => a.ConstructorArguments)
			.Select(ca => ca.Value)
			.OfType<ISymbol>();

		return typeSymbols.Any(ts => SymbolEqualityComparer.Default.Equals(ts, componentType));
	}

	private static bool IsInvocationNullChecked(InvocationExpressionSyntax invocation)
	{
		var parent = invocation.Parent;
		return parent switch
		{
			BinaryExpressionSyntax binary when
				(binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression))
				&& (binary.Right.IsKind(SyntaxKind.NullLiteralExpression) || binary.Left.IsKind(SyntaxKind.NullLiteralExpression)) => true,
			_ => false,
		};
	}
}