/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Generic;
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
using UnityFramework = TarragonFly.Analyzers.Frameworks.Unity;

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

		if (IsContainerRequiredBy(componentType, containerType))
			return;

		if (IsInvocationNullChecked(invocation) || IsResultOptional(model, invocation, context.CancellationToken))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	// The result goes into a local, field or property that the class treats as optional:
	// - the member holding the call tests it for null after the call;
	// - it is a serialized field or a [field: SerializeField] property and the call is its
	//   fallback, assigned inside "if (x == null)" or through "x ??=";
	// - it is a private field that another member tests for null;
	// - it is a private field that is only compared with == or != and never dereferenced.
	// A null test that guards the assignment, as in "if (x == null) x = GetComponent<T>();", is
	// lazy initialization and doesn't count.
	// Only the part of a partial class that holds the call is read. Revisit when a game splits a
	// MonoBehaviour over several files and gets a wrong finding from it.
	private static bool IsResultOptional(SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct)
	{
		SyntaxNode stored = invocation;
		while (stored.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
			stored = stored.Parent;

		ISymbol? target = stored.Parent switch
		{
			EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => model.GetDeclaredSymbol(declarator, ct),
			AssignmentExpressionSyntax assignment when assignment.Right == stored && (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)) => model.GetSymbolInfo(assignment.Left, ct).Symbol,
			_ => null,
		};
		if (target == null || invocation.FirstAncestorOrSelf<MemberDeclarationSyntax>() is not { } member)
			return false;

		if (ReferencesTo(target, member, model, ct).Any(name => name.SpanStart > invocation.SpanStart && IsNullCheck(name)))
			return true;

		if (IsSerializedByUnity(target) && IsFallbackAssignment(target, stored, member, model, ct))
			return true;

		if (target is not IFieldSymbol { DeclaredAccessibility: Accessibility.Private } || member.Parent is not TypeDeclarationSyntax type)
			return false;

		List<IdentifierNameSyntax> references = ReferencesTo(target, type, model, ct).ToList();
		List<IdentifierNameSyntax> uses = references.Where(name => !IsAssignedTo(name)).ToList();
		return uses.Any(name => !member.Span.Contains(name.Span) && IsNullCheck(name) && !GuardsAssignment(name, references))
			|| (uses.Count > 0 && uses.All(IsEqualityOperand));
	}

	// "if (x == null) x = ..." in another member is lazy initialization too, and its null test
	// doesn't count.
	private static bool GuardsAssignment(IdentifierNameSyntax name, List<IdentifierNameSyntax> references)
	{
		return name.Ancestors()
			.OfType<IfStatementSyntax>()
			.Any(ifStatement => ifStatement.Condition.Span.Contains(name.Span)
				&& references.Any(reference => IsAssignedTo(reference) && ifStatement.Statement.Span.Contains(reference.Span)));
	}

	private static IEnumerable<IdentifierNameSyntax> ReferencesTo(ISymbol symbol, SyntaxNode scope, SemanticModel model, CancellationToken ct)
	{
		return scope.DescendantNodesAndSelf()
			.OfType<IdentifierNameSyntax>()
			.Where(name => name.Identifier.ValueText == symbol.Name && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name, ct).Symbol, symbol));
	}

	private static bool IsSerializedByUnity(ISymbol target)
	{
		IFieldSymbol? field = target switch
		{
			IFieldSymbol declared => declared,
			IPropertySymbol property => property.ContainingType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(backing => SymbolEqualityComparer.Default.Equals(backing.AssociatedSymbol, property)),
			_ => null,
		};
		return field != null && UnityFramework.IsSerializedByUnity(field);
	}

	// "x ??= GetComponent<T>()", or the call is in the body of an "if" whose condition tests x for null.
	private static bool IsFallbackAssignment(ISymbol target, SyntaxNode stored, SyntaxNode member, SemanticModel model, CancellationToken ct)
	{
		if (stored.Parent.IsKind(SyntaxKind.CoalesceAssignmentExpression))
			return true;

		return stored.Ancestors()
			.TakeWhile(node => node != member)
			.OfType<IfStatementSyntax>()
			.Any(ifStatement => ifStatement.Statement.Span.Contains(stored.Span) && ReferencesTo(target, ifStatement.Condition, model, ct).Any(IsNullCheck));
	}

	// The name with its "this." in front, when it is written that way.
	private static SyntaxNode WithThisQualifier(IdentifierNameSyntax name)
	{
		return name.Parent is MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access && access.Name == name ? access : name;
	}

	private static bool IsAssignedTo(IdentifierNameSyntax name)
	{
		SyntaxNode written = WithThisQualifier(name);
		return written.Parent is AssignmentExpressionSyntax assignment && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) && assignment.Left == written;
	}

	private static bool IsEqualityOperand(IdentifierNameSyntax name)
	{
		SyntaxNode? comparison = WithThisQualifier(name).Parent;
		return comparison.IsKind(SyntaxKind.EqualsExpression) || comparison.IsKind(SyntaxKind.NotEqualsExpression);
	}

	// "x == null", "x != null", "x is null", "x?.", "x ?? ...", and Unity's "if (x)" or "!x".
	private static bool IsNullCheck(IdentifierNameSyntax name)
	{
		SyntaxNode tested = WithThisQualifier(name);
		return tested.Parent switch
		{
			BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)
				=> binary.Left.IsKind(SyntaxKind.NullLiteralExpression) || binary.Right.IsKind(SyntaxKind.NullLiteralExpression),
			BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression) => coalesce.Left == tested,
			BinaryExpressionSyntax logical => logical.IsKind(SyntaxKind.LogicalAndExpression) || logical.IsKind(SyntaxKind.LogicalOrExpression),
			IsPatternExpressionSyntax => true,
			ConditionalAccessExpressionSyntax conditional => conditional.Expression == tested,
			PrefixUnaryExpressionSyntax not => not.IsKind(SyntaxKind.LogicalNotExpression),
			IfStatementSyntax ifStatement => ifStatement.Condition == tested,
			ConditionalExpressionSyntax conditional => conditional.Condition == tested,
			_ => false,
		};
	}

	// [RequireComponent] is inherited, so a base class's attribute counts too. Upstream only read the
	// class's own attributes and reported components a base class already requires.
	private static bool IsTypeAlreadyRequired(ITypeSymbol requiringType, ITypeSymbol requiredType)
	{
		for (var type = requiringType; type != null; type = type.BaseType)
		{
			if (IsTypeRequiredBy(type, requiredType))
				return true;
		}

		return false;
	}

	// The component already requires this class, or a base class of it. Two components that
	// require each other can't be removed in the Inspector.
	private static bool IsContainerRequiredBy(ITypeSymbol componentType, ITypeSymbol containerType)
	{
		for (ITypeSymbol? type = containerType; type != null; type = type.BaseType)
		{
			if (IsTypeAlreadyRequired(componentType, type))
				return true;
		}

		return false;
	}

	private static bool IsTypeRequiredBy(ITypeSymbol requiringType, ITypeSymbol requiredType)
	{
		var typeSymbols = requiringType
			.GetAttributes()
			.Where(a => a.AttributeClass != null && a.AttributeClass.Matches(typeof(RequireComponent)))
			.SelectMany(a => a.ConstructorArguments)
			.Select(ca => ca.Value)
			.OfType<ISymbol>();

		return typeSymbols.Any(ts => SymbolEqualityComparer.Default.Equals(ts, requiredType));
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
