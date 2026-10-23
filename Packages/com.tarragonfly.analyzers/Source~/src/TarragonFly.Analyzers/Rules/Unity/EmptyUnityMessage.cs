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
public class EmptyUnityMessageAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4001";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.EmptyUnityMessageDiagnosticTitle,
		messageFormat: Strings.EmptyUnityMessageDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0001",
		description: Strings.EmptyUnityMessageDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMethodDeclaration), SyntaxKind.MethodDeclaration);
	}

	private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
	{
		var methodSyntax = context.Node as MethodDeclarationSyntax;
		if (methodSyntax?.Body == null)
			return;

		if (methodSyntax.HasPolymorphicModifier())
			return;

		if (methodSyntax.Body.Statements.Count > 0)
			return;

		if (context.SemanticModel.GetDeclaredSymbol(methodSyntax) is not { } methodSymbol)
			return;

		var scriptInfo = new ScriptInfo(methodSymbol.ContainingType);
		if (!scriptInfo.HasMessages)
			return;

		if (!scriptInfo.IsMessage(methodSymbol))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, methodSyntax.Identifier.GetLocation(), methodSymbol.Name));
	}
}