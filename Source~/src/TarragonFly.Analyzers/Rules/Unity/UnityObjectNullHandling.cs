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

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnityObjectNullHandlingAnalyzer : ScopedAnalyzer
{
	internal const string NullCoalescingRuleId = "TF4007";
	internal const string NullPropagationRuleId = "TF4008";
	internal const string CoalescingAssignmentRuleId = "TF4023";
	internal const string IsPatternRuleId = "TF4029";

	internal static readonly DiagnosticDescriptor NullCoalescingRule = Descriptors.Create(
		id: NullCoalescingRuleId,
		title: Strings.UnityObjectNullCoalescingDiagnosticTitle,
		messageFormat: Strings.UnityObjectNullCoalescingDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0007",
		description: Strings.UnityObjectNullCoalescingDiagnosticDescription);

	internal static readonly DiagnosticDescriptor NullPropagationRule = Descriptors.Create(
		id: NullPropagationRuleId,
		title: Strings.UnityObjectNullPropagationDiagnosticTitle,
		messageFormat: Strings.UnityObjectNullPropagationDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0008",
		description: Strings.UnityObjectNullPropagationDiagnosticDescription);

	internal static readonly DiagnosticDescriptor CoalescingAssignmentRule = Descriptors.Create(
		id: CoalescingAssignmentRuleId,
		title: Strings.UnityObjectCoalescingAssignmentDiagnosticTitle,
		messageFormat: Strings.UnityObjectCoalescingAssignmentDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0023",
		description: Strings.UnityObjectCoalescingAssignmentDiagnosticDescription);

	internal static readonly DiagnosticDescriptor IsPatternRule = Descriptors.Create(
		id: IsPatternRuleId,
		title: Strings.UnityObjectIsPatternDiagnosticTitle,
		messageFormat: Strings.UnityObjectIsPatternDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0029",
		description: Strings.UnityObjectIsPatternDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
		NullCoalescingRule,
		NullPropagationRule,
		CoalescingAssignmentRule,
		IsPatternRule
	);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeCoalesceExpression), SyntaxKind.CoalesceExpression);
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeConditionalAccessExpression), SyntaxKind.ConditionalAccessExpression);
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeCoalesceAssignmentExpression), SyntaxKind.CoalesceAssignmentExpression);
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeIsPatternExpression), SyntaxKind.IsPatternExpression);
	}

	private static void AnalyzeIsPatternExpression(SyntaxNodeAnalysisContext context)
	{
		var pattern = (IsPatternExpressionSyntax)context.Node;

		switch (pattern.Pattern)
		{
			// obj is null
			case ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression }:

			//obj is not null, we need roslyn 3.7.0 here for UnaryPatternSyntax type and SyntaxKind.NotPattern enum value
			case UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern, Pattern: ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression } }:
				AnalyzeExpression(pattern, pattern.Expression, context, IsPatternRule);
				break;
		}
	}

	private static void AnalyzeCoalesceAssignmentExpression(SyntaxNodeAnalysisContext context)
	{
		var assignment = (AssignmentExpressionSyntax)context.Node;
		AnalyzeExpression(assignment, assignment.Left, context, CoalescingAssignmentRule);
	}

	private static void AnalyzeCoalesceExpression(SyntaxNodeAnalysisContext context)
	{
		var binary = (BinaryExpressionSyntax)context.Node;
		AnalyzeExpression(binary, binary.Left, context, NullCoalescingRule);
	}

	private static void AnalyzeConditionalAccessExpression(SyntaxNodeAnalysisContext context)
	{
		var access = (ConditionalAccessExpressionSyntax)context.Node;
		AnalyzeExpression(access, access.Expression, context, NullPropagationRule);
	}

	private static void AnalyzeExpression(ExpressionSyntax originalExpression, ExpressionSyntax typedExpression, SyntaxNodeAnalysisContext context, DiagnosticDescriptor rule)
	{
		var type = context.SemanticModel.GetTypeInfo(typedExpression);
		if (type.Type == null)
			return;

		if (!type.Type.Extends(typeof(UnityEngine.Object)))
			return;

		context.ReportDiagnostic(Diagnostic.Create(rule, originalExpression.GetLocation(), originalExpression.ToFullString()));
	}
}