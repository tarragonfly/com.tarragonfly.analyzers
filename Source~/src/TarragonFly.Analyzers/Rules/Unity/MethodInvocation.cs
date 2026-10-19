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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MethodInvocationAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4016";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.MethodInvocationDiagnosticTitle,
		messageFormat: Strings.MethodInvocationDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0016",
		description: Strings.MethodInvocationDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	// TODO we cannot add this to our stubs/KnownMethods so far (else they will be matched as Unity messages)
	internal static readonly HashSet<string> InvokeMethodNames = ["Invoke", "InvokeRepeating", "CancelInvoke", "IsInvoking"];
	internal static readonly HashSet<string> CoroutineMethodNames = ["StartCoroutine", "StopCoroutine"];

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
				return InvokeMethodNames.Contains(text) || CoroutineMethodNames.Contains(text);
			default:
				return false;
		}
	}

	internal static bool InvocationMatches(InvocationExpressionSyntax ies, [NotNullWhen(true)] out string? argument)
	{
		argument = null;

		if (!InvocationMatches(ies))
			return false;

		var args = ies.ArgumentList.Arguments;

		if (args.Count <= 0)
			return false;

		if (args.First().Expression is not LiteralExpressionSyntax les)
			return false;

		argument = les.Token.ValueText;

		return true;
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not InvocationExpressionSyntax invocation)
			return;

		var options = invocation.SyntaxTree.Options as CSharpParseOptions;
		if (options == null || options.LanguageVersion < LanguageVersion.CSharp6) // we want nameof support
			return;

		if (!InvocationMatches(invocation, out string? argument))
			return;

		var model = context.SemanticModel;
		if (model.GetSymbolInfo(invocation.Expression).Symbol is not IMethodSymbol methodSymbol)
			return;

		var typeSymbol = methodSymbol.ContainingType;
		if (!typeSymbol.Extends(typeof(UnityEngine.MonoBehaviour)))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), argument));
	}
}