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
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class TryGetComponentAnalyzer : BaseGetComponentAnalyzer
{
	private const string RuleId = "TF4026";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.TryGetComponentDiagnosticTitle,
		messageFormat: Strings.TryGetComponentDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.Game,
		sourceRule: "UNT0026",
		description: Strings.TryGetComponentDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		if (!IsTryGetComponentSupported(context))
			return;

		var invocation = (InvocationExpressionSyntax)context.Node;
		var tgcContext = TryGetComponentContext.GetContext(invocation, context.SemanticModel);
		if (tgcContext == null)
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
	}
}

internal class TryGetComponentContext(string targetIdentifier, IfStatementSyntax ifStatement, SyntaxKind conditionKind)
{
	public string TargetIdentifier { get; } = targetIdentifier;
	public IfStatementSyntax IfStatement { get; } = ifStatement;
	public SyntaxKind ConditionKind { get; } = conditionKind;

	public static TryGetComponentContext? GetContext(InvocationExpressionSyntax invocation, SemanticModel model)
	{
		// We want the generic GetComponent, no arguments
		if (!BaseGetComponentAnalyzer.IsGenericGetComponent(invocation, model, out _))
			return null;

		// We want a variable declaration with invocation as initializer
		var invocationParent = invocation.Parent;
		if (invocationParent == null)
			return null;

		if (!BaseGetComponentAnalyzer.TryGetTargetdentifier(invocationParent, out var targetIdentifier))
			return null;

		// We want the next line to be an if statement
		if (!BaseGetComponentAnalyzer.TryGetNextTopNode(invocationParent, out var ifNode))
			return null;

		// We want a binary expression
		if (!BaseGetComponentAnalyzer.TryGetConditionIdentifier(ifNode, out var conditionIdentifier, out var binaryExpression, out var ifStatement))
			return null;

		// Reusing the same identifier
		if (conditionIdentifier.Value.Text != targetIdentifier.Value.Text)
			return null;

		// We allow inline ifs without else clause
		var visitor = invocationParent;
		while (visitor != null)
		{
			if (visitor is BlockSyntax)
				break;

			if (visitor is IfStatementSyntax { Else: not null })
				return null;

			visitor = visitor.Parent;
		}

		return new TryGetComponentContext(targetIdentifier.Value.Text, ifStatement, binaryExpression.Kind());
	}
}