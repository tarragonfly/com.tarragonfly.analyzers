/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
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
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.Unity;

public abstract class BaseVectorConversionAnalyzer : ScopedAnalyzer
{
	protected abstract Type FromType { get; }
	protected abstract Type ToType { get; }

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{

		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeObjectCreation), SyntaxKind.ObjectCreationExpression);
	}

	private void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ObjectCreationExpressionSyntax ocSyntax)
			return;

		var model = context.SemanticModel;
		if (model.GetOperation(ocSyntax) is not IObjectCreationOperation ocOperation)
			return;

		if (ocOperation.Type is not { } operationType)
			return;

		if (!operationType.Matches(ToType))
			return;

		if (!CheckArguments(ocOperation))
			return;

		if (!CheckForNonAmbiguousReplacement(ocOperation, model))
			return;

		ReportDiagnostic(context, ocSyntax.GetLocation());
	}

	protected virtual bool CheckForNonAmbiguousReplacement(IObjectCreationOperation ocOperation, SemanticModel model)
	{
		if (ocOperation.Parent is not IArgumentOperation arOperation)
			return true;

		if (arOperation.Parent is not IInvocationOperation inOperation)
			return true;

		if (inOperation.Syntax is not InvocationExpressionSyntax invocation)
			return true;

		var overloads = model
			.GetMemberGroup(invocation.Expression)
			.OfType<IMethodSymbol>();

		// With Unity 6000.3.x, Unity introduced overloads with -in- specifiers for performance
		// So we consider overloads as distinct only when their parameter types differ (ignoring specifiers)
		var distinctSignatures = overloads
			.GroupBy(m => string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))))
			.Count();

		return distinctSignatures == 1;
	}

	protected virtual bool CheckArguments(IObjectCreationOperation ocOperation)
	{
		if (ocOperation.Arguments.Length < 2)
			return false;

		var first = ocOperation.Arguments[0];
		var second = ocOperation.Arguments[1];

		if (!ArgumentMatches(first, "x"))
			return false;

		if (!ArgumentMatches(second, "y"))
			return false;

		var firstIdentifier = GetIdentifierNameSyntax(first);
		var secondIdentifier = GetIdentifierNameSyntax(second);

		if (firstIdentifier == null || secondIdentifier == null || firstIdentifier.Identifier.Text != secondIdentifier.Identifier.Text)
			return false;

		return true;
	}

	protected abstract void ReportDiagnostic(SyntaxNodeAnalysisContext context, Location location);

	private bool ArgumentMatches(IArgumentOperation argumentOperation, string name)
	{
		return argumentOperation.Value is IFieldReferenceOperation fieldOperation
			   && fieldOperation.Field.Name == name
			   && fieldOperation.Field.ContainingType.Matches(FromType);
	}

	internal static IdentifierNameSyntax? GetIdentifierNameSyntax(IArgumentOperation argumentOperation)
	{
		if (argumentOperation.Syntax is not ArgumentSyntax { Expression: MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax identifierNameSyntax } })
			return null;

		return identifierNameSyntax;
	}
}