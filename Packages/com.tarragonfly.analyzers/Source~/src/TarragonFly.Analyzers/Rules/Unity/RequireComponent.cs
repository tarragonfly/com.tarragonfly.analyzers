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
		if (!componentType.Extends(typeof(Component)))
			return;

		if (IsTypeAlreadyRequired(containerType, componentType))
			return;

		if (IsInvocationNullChecked(invocation) || IsNullCheckedLater(model, invocation, context.CancellationToken))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	// The result goes into a local, field or property that the method checks for null later on, so
	// the component is optional. Only the method holding the call is read; a field set in Awake and
	// checked in Update is still reported.
	private static bool IsNullCheckedLater(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
	{
		SyntaxNode stored = invocation;
		while (stored.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
			stored = stored.Parent;

		ISymbol? target = stored.Parent switch
		{
			EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => model.GetDeclaredSymbol(declarator, ct),
			AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Right == stored => model.GetSymbolInfo(assignment.Left, ct).Symbol,
			_ => null,
		};
		if (target == null || invocation.FirstAncestorOrSelf<MemberDeclarationSyntax>() is not { } member)
			return false;

		foreach (var name in member.DescendantNodes().OfType<IdentifierNameSyntax>())
		{
			if (name.SpanStart <= invocation.SpanStart || name.Identifier.ValueText != target.Name)
				continue;
			if (IsNullCheck(name) && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, ct).Symbol, target))
				return true;
		}

		return false;
	}

	// "x == null", "x != null", "x is null", "x?.", "x ?? ...", and Unity's "if (x)" or "!x".
	private static bool IsNullCheck(IdentifierNameSyntax name)
	{
		SyntaxNode checkedNode = name.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access && access.Name == name ? access : name;
		return checkedNode.Parent switch
		{
			BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)
				=> binary.Left.IsKind(SyntaxKind.NullLiteralExpression) || binary.Right.IsKind(SyntaxKind.NullLiteralExpression),
			BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression) => coalesce.Left == checkedNode,
			BinaryExpressionSyntax logical => logical.IsKind(SyntaxKind.LogicalAndExpression) || logical.IsKind(SyntaxKind.LogicalOrExpression),
			IsPatternExpressionSyntax => true,
			ConditionalAccessExpressionSyntax conditional => conditional.Expression == checkedNode,
			PrefixUnaryExpressionSyntax not => not.IsKind(SyntaxKind.LogicalNotExpression),
			IfStatementSyntax ifStatement => ifStatement.Condition == checkedNode,
			ConditionalExpressionSyntax conditional => conditional.Condition == checkedNode,
			_ => false,
		};
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
