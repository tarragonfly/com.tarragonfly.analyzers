/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
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

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MeshArrayPropertyInLoopAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4042";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.MeshArrayPropertyInLoopDiagnosticTitle,
		messageFormat: Strings.MeshArrayPropertyInLoopDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.Game,
		sourceRule: "UNT0042",
		description: Strings.MeshArrayPropertyInLoopDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	private static readonly Type[] AllocatingArrayElementTypes =
	[
		typeof(UnityEngine.Vector2), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector4), typeof(UnityEngine.Color), typeof(UnityEngine.Color32)
	];

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMemberAccess), SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MemberAccessExpressionSyntax memberAccess)
			return;

		var loop = FindContainingLoop(memberAccess);
		if (loop == null)
			return;

		if (!IsAllocatingMeshProperty(context, memberAccess, out var propertyName))
			return;

		// Only report if this is the first occurrence of this property access in the loop
		// to avoid flooding the user with multiple diagnostics for the same issue
		if (!IsFirstOccurrenceInLoop(context.SemanticModel, memberAccess, loop))
			return;

		context.ReportDiagnostic(Diagnostic.Create(
			Rule,
			memberAccess.GetLocation(),
			propertyName));
	}

	internal static SyntaxNode? FindContainingLoop(SyntaxNode node)
	{
		var current = node.Parent;
		while (current != null)
		{
			switch (current)
			{
				case ForStatementSyntax:
				case ForEachStatementSyntax:
				case WhileStatementSyntax:
				case DoStatementSyntax:
					return current;

				// Stop looking if we hit a method/lambda/function boundary
				case MethodDeclarationSyntax:
				case LocalFunctionStatementSyntax:
				case AnonymousFunctionExpressionSyntax:
					return null;
			}

			current = current.Parent;
		}

		return null;
	}

	private static bool IsFirstOccurrenceInLoop(SemanticModel model, MemberAccessExpressionSyntax memberAccess, SyntaxNode loop)
	{
		var allOccurrences = loop.DescendantNodes()
			.OfType<MemberAccessExpressionSyntax>()
			.Where(m => IsInLoopConditionOrBody(m, loop) && IsSameMeshPropertyAccess(model, m, memberAccess))
			.OrderBy(m => m.SpanStart)
			.ToList();

		return allOccurrences.Count > 0 && allOccurrences[0] == memberAccess;
	}

	internal static bool IsSameMeshPropertyAccess(SemanticModel model, MemberAccessExpressionSyntax candidate, MemberAccessExpressionSyntax reference)
	{
		var candidateSymbol = model.GetSymbolInfo(candidate).Symbol;
		var referenceSymbol = model.GetSymbolInfo(reference).Symbol;

		if (candidateSymbol == null || referenceSymbol == null)
			return false;

		if (!SymbolEqualityComparer.Default.Equals(candidateSymbol, referenceSymbol))
			return false;

		var candidateExprSymbol = model.GetSymbolInfo(candidate.Expression).Symbol;
		var referenceExprSymbol = model.GetSymbolInfo(reference.Expression).Symbol;

		return SymbolEqualityComparer.Default.Equals(candidateExprSymbol, referenceExprSymbol);
	}

	internal static bool IsInLoopConditionOrBody(SyntaxNode node, SyntaxNode loop)
	{
		if (!loop.Contains(node))
			return false;

		var current = node.Parent;
		while (current != null && current != loop)
		{
			switch (current)
			{
				case MethodDeclarationSyntax:
				case LocalFunctionStatementSyntax:
				case AnonymousFunctionExpressionSyntax:
					return false;
			}

			current = current.Parent;
		}

		return loop switch
		{
			ForStatementSyntax forStatement =>
				IsInLoopConditionOrIncrementors(node, forStatement) || forStatement.Statement.Contains(node),
			ForEachStatementSyntax forEachStatement =>
				forEachStatement.Statement.Contains(node),
			WhileStatementSyntax whileStatement =>
				whileStatement.Condition.Contains(node) || whileStatement.Statement.Contains(node),
			DoStatementSyntax doStatement =>
				doStatement.Condition.Contains(node) || doStatement.Statement.Contains(node),
			_ => false
		};
	}

	private static bool IsInLoopConditionOrIncrementors(SyntaxNode node, ForStatementSyntax forStatement)
	{
		if (forStatement.Condition != null && forStatement.Condition.Contains(node))
			return true;

		foreach (var incrementor in forStatement.Incrementors)
		{
			if (incrementor.Contains(node))
				return true;
		}

		return false;
	}

	internal static bool IsAllocatingMeshProperty(SyntaxNodeAnalysisContext context, MemberAccessExpressionSyntax memberAccess, out string? propertyName)
	{
		return IsAllocatingMeshProperty(context.SemanticModel, memberAccess, out propertyName);
	}

	internal static bool IsAllocatingMeshProperty(SemanticModel model, MemberAccessExpressionSyntax memberAccess, out string? propertyName)
	{
		propertyName = null;

		var symbol = model.GetSymbolInfo(memberAccess).Symbol;
		if (symbol is not IPropertySymbol propertySymbol)
			return false;

		var containingType = propertySymbol.ContainingType;
		if (!containingType.Matches(typeof(UnityEngine.Mesh)))
			return false;

		if (propertySymbol.Type is not IArrayTypeSymbol arrayType)
			return false;

		var element = arrayType.ElementType;
		if (AllocatingArrayElementTypes.All(t => !element.Matches(t)))
			return false;

		propertyName = propertySymbol.Name;
		return true;
	}
}