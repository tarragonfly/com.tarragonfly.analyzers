/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
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
public class ImproperMessageCaseAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4033";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.ImproperMessageCaseDiagnosticTitle,
		messageFormat: Strings.ImproperMessageCaseDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0033",
		description: Strings.ImproperMessageCaseDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeClassDeclaration), SyntaxKind.ClassDeclaration);
	}

	private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ClassDeclarationSyntax classSyntax)
			return;

		if (context.SemanticModel.GetDeclaredSymbol(classSyntax) is not ITypeSymbol typeSymbol)
			return;

		var scriptInfo = new ScriptInfo(typeSymbol);
		if (!scriptInfo.HasMessages)
			return;

		var methods = classSyntax.Members.OfType<MethodDeclarationSyntax>();

		var allMessages = scriptInfo
			.GetMessages()
			.ToLookup(m => m.Name); // case-sensitive indexing

		var notImplementedMessages = scriptInfo
			.GetNotImplementedMessages()
			.ToLookup(m => m.Name.ToLower()); // case-insensitive indexing

		foreach (var methodSyntax in methods)
		{
			if (methodSyntax.ExplicitInterfaceSpecifier != null)
				continue;

			if (methodSyntax.HasPolymorphicModifier())
				continue;

			var methodName = methodSyntax.Identifier.Text;
			// We have a valid case match here, so stop further inspection (This will prevent false positive for possible overloads, when one of them is still in the notImplementedMessages lookup)
			if (allMessages.Contains(methodName))
				continue;

			var key = methodName.ToLower();
			if (!notImplementedMessages.Contains(key))
				continue;

			if (context.SemanticModel.GetDeclaredSymbol(methodSyntax) is not { } methodSymbol)
				continue;

			var namedMessages = notImplementedMessages[key];
			if (namedMessages.All(m => m.IsStatic != methodSymbol.IsStatic))
				continue;

			// We can't use SymbolFinder.FindReferencesAsync() to find possible references, given we do not have access to the solution here yet
			context.ReportDiagnostic(Diagnostic.Create(Rule, methodSyntax.Identifier.GetLocation(), methodName));
		}
	}
}