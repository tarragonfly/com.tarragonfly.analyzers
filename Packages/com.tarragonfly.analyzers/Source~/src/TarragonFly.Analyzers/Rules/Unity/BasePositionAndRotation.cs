/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using UnityEngine;
using UnityEngine.Jobs;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TarragonFly.Analyzers.Rules.Unity;

public abstract class BasePositionAndRotationContext(string positionPropertyName, string rotationPropertyName, string positionAndRotationMethodName)
{
	public string PositionPropertyName { get; } = positionPropertyName;
	public string RotationPropertyName { get; } = rotationPropertyName;
	public string PositionAndRotationMethodName { get; } = positionAndRotationMethodName;

	public abstract bool TryGetPropertyExpression(SemanticModel model, ExpressionSyntax expression, [NotNullWhen(true)] out MemberAccessExpressionSyntax? result);
	public abstract bool TryGetArgumentExpression(SemanticModel model, ExpressionSyntax expression, [NotNullWhen(true)] out ArgumentSyntax? result);

	private bool IsPositionOrRotationCandidate(SemanticModel model, ExpressionSyntax expression)
	{
		var property = GetPropertyName(model, expression);
		if (property != PositionPropertyName && property != RotationPropertyName)
			return false;

		if (!TryGetPropertyExpression(model, expression, out var syntax))
			return false;

		if (!TryGetArgumentExpression(model, expression, out _))
			return false;

		var symbolInfo = model.GetSymbolInfo(syntax);
		if (symbolInfo.Symbol is not IPropertySymbol)
			return false;

		var expressionTypeInfo = model.GetTypeInfo(syntax.Expression);
		if (expressionTypeInfo.Type == null)
			return false;

		return expressionTypeInfo.Type.Extends(typeof(Transform))
			|| expressionTypeInfo.Type.Extends(typeof(TransformAccess));
	}

	public virtual bool TryGetExpression(SyntaxNode node, [NotNullWhen(true)] out ExpressionSyntax? result)
	{
		if (node is ExpressionStatementSyntax statement)
			node = statement.Expression;

		result = node as AssignmentExpressionSyntax;
		return result != null;
	}

	public bool TryGetNextExpression(SemanticModel model, ExpressionSyntax expression, [NotNullWhen(true)] out ExpressionSyntax? nextExpression)
	{
		nextExpression = null;

		if (!IsPositionOrRotationCandidate(model, expression))
			return false;

		if (expression.FirstAncestorOrSelf<BlockSyntax>() == null)
			return false;

		if (expression.FirstAncestorOrSelf<StatementSyntax>() == null)
			return false;

		var block = expression.FirstAncestorOrSelf<BlockSyntax>();
		if (block == null)
			return false;

		var siblingsAndSelf = block.ChildNodes().ToImmutableArray();

		var candidate = expression.FirstAncestorOrSelf<StatementSyntax>();
		if (candidate == null)
			return false;

		var lastIndexOf = siblingsAndSelf.LastIndexOf(candidate);
		if (lastIndexOf == -1)
			return false;

		var nextIndex = lastIndexOf + 1;
		if (nextIndex == siblingsAndSelf.Length)
			return false;

		var statement = siblingsAndSelf[nextIndex];
		if (!TryGetExpression(statement, out var nextExpressionCandidate))
			return false;

		if (!IsPositionOrRotationCandidate(model, nextExpressionCandidate))
			return false;

		nextExpression = nextExpressionCandidate;
		return true;
	}

	public string GetPropertyName(SemanticModel model, ExpressionSyntax expression)
	{
		if (!TryGetPropertyExpression(model, expression, out var syntax))
			return string.Empty;

		return syntax.Name.ToString();
	}
}

public abstract class BasePositionAndRotationAnalyzer(BasePositionAndRotationContext expressionContext) : ScopedAnalyzer
{
	internal BasePositionAndRotationContext ExpressionContext { get; } = expressionContext;

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeExpression), SyntaxKind.SimpleAssignmentExpression);
	}

	protected void AnalyzeExpression(SyntaxNodeAnalysisContext context)
	{
		if (!ExpressionContext.TryGetExpression(context.Node, out var expression))
			return;

		var model = context.SemanticModel;
		if (!ExpressionContext.TryGetNextExpression(model, expression, out var nextExpression))
			return;

		if (!ExpressionContext.TryGetPropertyExpression(model, expression, out var syntax))
			return;

		if (!ExpressionContext.TryGetPropertyExpression(model, nextExpression, out var nextSyntax))
			return;

		if (!IsStableReceiver(model, syntax.Expression) || !IsStableReceiver(model, nextSyntax.Expression)) return;
		if (ExpressionContext is BaseSetPositionAndRotationContext
			&& (HasSideEffects(model, ((AssignmentExpressionSyntax)expression).Right) || HasSideEffects(model, ((AssignmentExpressionSyntax)nextExpression).Right))) return;

		if (!AreExpressionsEquivalent(model, syntax.Expression, nextSyntax.Expression))
			return;

		var property = ExpressionContext.GetPropertyName(model, expression);
		var nextProperty = ExpressionContext.GetPropertyName(model, nextExpression);
		if (property == nextProperty)
			return;

		// Check that the replacement method exists on target type in the current Unity version
		var type = model.GetTypeInfo(syntax.Expression).Type;
		if (type == null || !type.GetMembers().Any(m => m is IMethodSymbol && m.Name == ExpressionContext.PositionAndRotationMethodName))
			return;

		if (expression.FirstAncestorOrSelf<StatementSyntax>() is not { } statement)
			return;

		// Check for expression reuse that could change semantics when combining into a single method call
		if (DetectExpressionReuse(model, syntax, nextSyntax))
			return;

		OnPatternFound(context, statement);
	}

	// Other properties, such as Camera.main or our own getters, can hand back a different object on
	// each read, so only Unity's transform and gameObject count as the same object twice.
	private static bool IsStableReceiver(SemanticModel model, ExpressionSyntax expression)
	{
		if (expression is ThisExpressionSyntax or BaseExpressionSyntax) return true;
		ISymbol? symbol = model.GetSymbolInfo(expression).Symbol;
		if (symbol is ILocalSymbol local) return !local.IsRef;
		if (symbol is IParameterSymbol parameter) return parameter.RefKind == RefKind.None;
		if (symbol is IPropertySymbol property && !(property.Name is "transform" or "gameObject"
			&& (property.ContainingType.Matches(typeof(Component)) || property.ContainingType.Matches(typeof(GameObject))))) return false;
		if (symbol is not IFieldSymbol && symbol is not IPropertySymbol) return false;
		return expression is IdentifierNameSyntax || expression is MemberAccessExpressionSyntax access && IsStableReceiver(model, access.Expression);
	}

	// Property getters count as plain reads, so "speed * Time.deltaTime" doesn't block the merge.
	// Random.Range does change the random state, but it can't see the transform, so it's fine too.
	private static bool HasSideEffects(SemanticModel model, ExpressionSyntax expression)
	{
		IOperation? operation = model.GetOperation(expression);
		return operation == null || operation.DescendantsAndSelf().Any(child => child is IAssignmentOperation or IIncrementOrDecrementOperation or IAwaitOperation
			|| child is IInvocationOperation call && !IsUnityValueType(call.TargetMethod.ContainingType) && !IsUnityRandom(call.TargetMethod)
			|| child is IObjectCreationOperation creation && !IsUnityValueType(creation.Type));
	}

	private static bool IsUnityRandom(IMethodSymbol method)
	{
		return method.IsStatic && method.ContainingType.ToDisplayString() == "UnityEngine.Random";
	}

	private static bool IsUnityValueType(ITypeSymbol? type)
	{
		return type?.IsValueType == true && type.ContainingNamespace.ToDisplayString() == "UnityEngine";
	}

	private static bool AreExpressionsEquivalent(SemanticModel model, ExpressionSyntax expr1, ExpressionSyntax expr2)
	{
		// First try syntactic equivalence which handles cases like "transform" vs "this.transform"
		if (expr1.IsEquivalentTo(expr2))
			return true;

		// For member access expressions, we need to compare the entire chain semantically
		if (expr1 is MemberAccessExpressionSyntax member1 && expr2 is MemberAccessExpressionSyntax member2)
		{
			// Compare the member name symbols (e.g., "transform" property)
			var symbol1 = model.GetSymbolInfo(member1.Name).Symbol;
			var symbol2 = model.GetSymbolInfo(member2.Name).Symbol;

			if (symbol1 == null || symbol2 == null)
				return false;

			return SymbolEqualityComparer.Default.Equals(symbol1, symbol2) &&
				   AreExpressionsEquivalent(model, member1.Expression, member2.Expression);
		}

		// For other expressions, compare their symbols
		var exprSymbol1 = model.GetSymbolInfo(expr1).Symbol;
		var exprSymbol2 = model.GetSymbolInfo(expr2).Symbol;

		// If either symbol is null (including ambiguous cases with CandidateSymbols),
		// we conservatively treat them as not equivalent to avoid incorrect suggestions
		if (exprSymbol1 == null || exprSymbol2 == null)
			return false;

		return SymbolEqualityComparer.Default.Equals(exprSymbol1, exprSymbol2);
	}

	private static bool DetectExpressionReuse(SemanticModel model, MemberAccessExpressionSyntax candidate, MemberAccessExpressionSyntax expression)
	{
		var syntaxes = expression.Parent?.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
		return syntaxes != null && syntaxes.Any(syntax => AreExpressionsEquivalent(model, candidate, syntax));
	}

	protected abstract void OnPatternFound(SyntaxNodeAnalysisContext context, StatementSyntax statement);
}
