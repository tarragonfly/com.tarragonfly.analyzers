/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
public class DestroyTransformAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4030";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.DestroyTransformDiagnosticTitle,
		messageFormat: Strings.DestroyTransformDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0030",
		description: Strings.DestroyTransformDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	internal static readonly HashSet<string> DestroyMethodNames = ["Destroy", "DestroyImmediate"];

	private static bool InvocationMatches(SyntaxNode node)
	{
		switch (node)
		{
			case InvocationExpressionSyntax ies:
				return InvocationMatches(ies.Expression);
			case MemberAccessExpressionSyntax maes:
				return InvocationMatches(maes.Name);
			case IdentifierNameSyntax ins:
				var text = ins.Identifier.Text;
				return DestroyMethodNames.Contains(text);
			default:
				return false;
		}
	}

	internal static bool InvocationMatches(InvocationExpressionSyntax invocation, [NotNullWhen(true)] out ExpressionSyntax? argument)
	{
		argument = null;

		if (!InvocationMatches(invocation))
			return false;

		argument = invocation
			.ArgumentList
			.Arguments
			.FirstOrDefault()?
			.Expression;

		return argument != null;
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not InvocationExpressionSyntax invocation)
			return;

		if (!InvocationMatches(invocation, out var argument))
			return;

		var model = context.SemanticModel;
		if (model.GetSymbolInfo(invocation.Expression).Symbol is not IMethodSymbol methodSymbol)
			return;

		var typeSymbol = methodSymbol.ContainingType;
		if (!typeSymbol.Matches(typeof(UnityEngine.Object)))
			return;

		var transformTypeSymbol = model
			.GetTypeInfo(argument)
			.Type;

		if (!transformTypeSymbol.Extends(typeof(UnityEngine.Transform)))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}
}