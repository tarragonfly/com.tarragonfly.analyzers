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
public class ImproperMenuItemMethodAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4020";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.ImproperMenuItemMethodDiagnosticTitle,
		messageFormat: Strings.ImproperMenuItemMethodDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0020",
		description: Strings.ImproperMenuItemMethodDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMethodDeclaration), SyntaxKind.MethodDeclaration);
	}

	private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MethodDeclarationSyntax method)
			return;

		if (context.SemanticModel.GetDeclaredSymbol(method) is not { } methodSymbol)
			return;

		if (!methodSymbol.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.Matches(typeof(UnityEditor.MenuItem))))
			return;

		if (methodSymbol.IsStatic)
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, method.GetLocation(), method));
	}
}