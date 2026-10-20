/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnusedCoroutineReturnValueAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4012";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.UnusedCoroutineReturnValueDiagnosticTitle,
		messageFormat: Strings.UnusedCoroutineReturnValueDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0012",
		description: Strings.UnusedCoroutineReturnValueDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		if (!invocation.Parent.IsKind(SyntaxKind.ExpressionStatement))
			return;

		var symbol = context.SemanticModel.GetSymbolInfo(invocation);
		if (symbol.Symbol == null)
			return;

		if (!IsValidCoroutine(symbol.Symbol, out var methodName))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), methodName));
	}

	private static bool IsValidCoroutine(ISymbol symbol, out string? methodName)
	{
		methodName = null;
		if (symbol is not IMethodSymbol method)
			return false;

		var containingType = method.ContainingType;
		if (!containingType.Extends(typeof(UnityEngine.MonoBehaviour)))
			return false;

		if (!method.ReturnType.Matches(typeof(System.Collections.IEnumerator)))
			return false;

		methodName = method.Name;
		return true;
	}
}