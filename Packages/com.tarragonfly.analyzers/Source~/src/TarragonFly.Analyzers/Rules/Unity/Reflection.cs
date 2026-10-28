/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReflectionAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4018";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.ReflectionDiagnosticTitle,
		messageFormat: Strings.ReflectionDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0018",
		description: Strings.ReflectionDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{

		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMethodDeclaration), SyntaxKind.MethodDeclaration);
	}

	private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MethodDeclarationSyntax method)
			return;

		if (!IsCriticalMessage(context, method))
			return;

		AnalyzeMethodBody(context, method);
	}

	private static bool IsCriticalMessage(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax methodSyntax)
	{
		if (context.SemanticModel.GetDeclaredSymbol(methodSyntax) is not { } methodSymbol)
			return false;

		var scriptInfo = new ScriptInfo(methodSymbol.ContainingType);
		if (!scriptInfo.HasMessages)
			return false;

		if (!scriptInfo.IsMessage(methodSymbol))
			return false;

		return methodSymbol.Name switch
		{
			"Update" or "FixedUpdate" or "LateUpdate" or "OnGUI" => true,
			_ => false,
		};
	}

	private static void AnalyzeMethodBody(SyntaxNodeAnalysisContext context, MethodDeclarationSyntax method)
	{
		var srns = typeof(Assembly).Namespace;

		SyntaxNode? body = method.Body ?? (SyntaxNode?)method.ExpressionBody;
		if (body == null)
			return;

		foreach (var node in body.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
		{
			var typeInfo = context.SemanticModel.GetTypeInfo(node);
			var typeSymbol = typeInfo.Type;

			if (typeSymbol?.ContainingNamespace == null)
				continue;

			if (typeSymbol.ContainingNamespace.ToDisplayString() != srns)
				continue;

			context.ReportDiagnostic(Diagnostic.Create(Rule, node.GetLocation(), method.Identifier.Text));
			return;
		}
	}
}
