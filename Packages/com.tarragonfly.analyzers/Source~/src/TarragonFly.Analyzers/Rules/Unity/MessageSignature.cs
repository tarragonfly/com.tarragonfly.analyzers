/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

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

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MessageSignatureAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4006";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.MessageSignatureDiagnosticTitle,
		messageFormat: Strings.MessageSignatureDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0006",
		description: Strings.MessageSignatureDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeClassDeclaration), SyntaxKind.ClassDeclaration);
	}

	private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ClassDeclarationSyntax classSyntax)
			return;

		if (context.SemanticModel.GetDeclaredSymbol(classSyntax) is not { } typeSymbol)
			return;

		var scriptInfo = new ScriptInfo(typeSymbol);
		if (!scriptInfo.HasMessages)
			return;

		var methods = classSyntax.Members.OfType<MethodDeclarationSyntax>();
		var messages = scriptInfo
			.GetMessages()
			.ToLookup(m => m.Name);

		foreach (var methodSyntax in methods)
		{
			// Exclude explicit interface implementations, that will not be called by the Unity runtime
			// scriptInfo.IsMessage is already handling this through fullname, but for this very specific task we are looking for bad signatures, so not relying on scriptInfo.IsMessage at this step
			if (methodSyntax.ExplicitInterfaceSpecifier != null)
				continue;

			var methodName = methodSyntax.Identifier.Text;

			if (!messages.Contains(methodName))
				continue;

			if (context.SemanticModel.GetDeclaredSymbol(methodSyntax) is not { } methodSymbol)
				continue;

			// A message is detected, so the signature is correct
			if (scriptInfo.IsMessage(methodSymbol))
				continue;

			// Check static/instance compatibility
			var namedMessages = messages[methodName];
			if (namedMessages.All(m => m.IsStatic != methodSymbol.IsStatic))
				continue;

			context.ReportDiagnostic(Diagnostic.Create(Rule, methodSyntax.Identifier.GetLocation(), methodName));
		}
	}
}