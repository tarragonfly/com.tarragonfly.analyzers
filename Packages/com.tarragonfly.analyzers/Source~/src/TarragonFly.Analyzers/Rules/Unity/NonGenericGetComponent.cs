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
public class NonGenericGetComponentAnalyzer : BaseGetComponentAnalyzer
{
	private const string RuleId = "TF4003";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.NonGenericGetComponentDiagnosticTitle,
		messageFormat: Strings.NonGenericGetComponentDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0003",
		description: Strings.NonGenericGetComponentDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		var name = invocation.GetMethodNameSyntax();
		if (name == null)
			return;

		if (!KnownMethods.IsGetComponentName(name))
			return;

		if (!IsNonGenericGetComponent(invocation, context.SemanticModel, out var method))
			return;

		if (invocation.Expression is not IdentifierNameSyntax)
			return;

		if (!invocation.ArgumentList.Arguments[0].Expression.IsKind(SyntaxKind.TypeOfExpression))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.Name));
	}
}