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
public class InitializeOnLoadStaticCtorAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4009";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.InitializeOnLoadStaticCtorDiagnosticTitle,
		messageFormat: Strings.InitializeOnLoadStaticCtorDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0009",
		description: Strings.InitializeOnLoadStaticCtorDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeClassDeclaration), SyntaxKind.ClassDeclaration);
	}

	private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not ClassDeclarationSyntax classDeclaration)
			return;

		var typeSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
		if (typeSymbol == null)
			return;

		if (!IsDecorated(typeSymbol))
			return;

		// Beware of compiler-generated ctor with static field initializers
		if (typeSymbol.StaticConstructors.Any(c => !c.IsImplicitlyDeclared))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, classDeclaration.Identifier.GetLocation(), typeSymbol.Name));
	}

	internal static bool IsDecorated(ITypeSymbol symbol)
	{
		return symbol
			.GetAttributes()
			.Any(a => a.AttributeClass != null && a.AttributeClass.Matches(typeof(UnityEditor.InitializeOnLoadAttribute)));
	}
}