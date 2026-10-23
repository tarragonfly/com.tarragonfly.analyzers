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
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AnimatorStringToHashAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4041";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.AnimatorStringToHashDiagnosticTitle,
		messageFormat: Strings.AnimatorStringToHashDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.Game,
		sourceRule: "UNT0041",
		description: Strings.AnimatorStringToHashDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;

		if (context.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol methodSymbol)
			return;

		var containingType = methodSymbol.ContainingType;

		if (containingType == null || !containingType.Matches(typeof(UnityEngine.Animator)))
			return;

		var stringArgument = FindStringArgumentWithHashOverload(invocation, methodSymbol);
		if (stringArgument?.Expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
			return;

		context.ReportDiagnostic(Diagnostic.Create(
			Rule,
			invocation.GetLocation(),
			methodSymbol.Name,
			literal.Token.ValueText));
	}

	internal static ArgumentSyntax? FindStringArgumentWithHashOverload(InvocationExpressionSyntax invocation, IMethodSymbol methodSymbol)
	{
		var arguments = invocation.ArgumentList.Arguments;

		for (var i = 0; i < methodSymbol.Parameters.Length && i < arguments.Count; i++)
		{
			var parameter = methodSymbol.Parameters[i];
			if (!parameter.Type.Matches(typeof(string)))
				continue;

			if (HasIntOverloadAtPosition(methodSymbol, i))
				return arguments[i];
		}

		return null;
	}

	internal static bool HasIntOverloadAtPosition(IMethodSymbol methodSymbol, int stringParameterIndex)
	{
		var containingType = methodSymbol.ContainingType;
		var overloads = containingType.GetMembers(methodSymbol.Name)
			.OfType<IMethodSymbol>()
			.Where(m => m.Parameters.Length == methodSymbol.Parameters.Length);

		return overloads
			.Where(overload => !SymbolEqualityComparer.Default.Equals(overload, methodSymbol))
			.Any(overload => overload.Parameters.Select((param, i) => i == stringParameterIndex
					? param.Type.SpecialType == SpecialType.System_Int32
					: SymbolEqualityComparer.Default.Equals(methodSymbol.Parameters[i].Type, param.Type))
				.All(match => match));
	}
}