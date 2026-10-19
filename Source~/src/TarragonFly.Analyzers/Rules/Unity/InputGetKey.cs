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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using UnityEngine;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class InputGetKeyAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4025";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.InputGetKeyDiagnosticTitle,
		messageFormat: Strings.InputGetKeyDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0025",
		description: Strings.InputGetKeyDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	private static readonly Lazy<Dictionary<string, KeyCode>> _lookup = new(BuildLookup);

	private static Dictionary<string, KeyCode> BuildLookup()
	{
		var enumType = typeof(KeyCode);
		var values = Enum.GetValues(enumType);
		var lookup = new Dictionary<string, KeyCode>();

		foreach (KeyCode item in values)
		{
			var fieldInfo = enumType.GetField(item.ToString());
			if (fieldInfo == null)
				continue;

			var attribute = fieldInfo.GetCustomAttributes<KeyTextAttribute>(false).FirstOrDefault();
			var key = attribute?.Text;
			if (key == null)
				continue;

			lookup.Add(key, item);
		}

		return lookup;
	}

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{

		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not InvocationExpressionSyntax invocation)
			return;

		if (!IsInvocationSupported(invocation))
			return;

		var symbol = context.SemanticModel.GetSymbolInfo(invocation);
		if (symbol.Symbol is not IMethodSymbol method)
			return;

		if (!IsMethodSupported(method))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}

	private static bool IsInvocationSupported(InvocationExpressionSyntax invocation)
	{
		if (invocation.ArgumentList.Arguments.Count != 1)
			return false;

		var argument = invocation.ArgumentList.Arguments.First();
		return IsArgumentSupported(argument);
	}

	private static bool IsMethodSupported(IMethodSymbol method)
	{
		return method.Name switch
		{
			nameof(Input.GetKey) or nameof(Input.GetKeyDown) or nameof(Input.GetKeyUp) => method.ContainingType.Matches(typeof(Input)),
			_ => false,
		};
	}

	private static bool IsArgumentSupported(ArgumentSyntax argument)
	{
		if (argument.Expression is not LiteralExpressionSyntax les)
			return false;

		if (les.Kind() != SyntaxKind.StringLiteralExpression)
			return false;

		return TryParse(les, out _);
	}

	internal static bool TryParse(LiteralExpressionSyntax les, out KeyCode value)
	{
		var name = les
			.Token
			.ValueText
			.ToLower();

		return _lookup.Value.TryGetValue(name, out value);
	}
}