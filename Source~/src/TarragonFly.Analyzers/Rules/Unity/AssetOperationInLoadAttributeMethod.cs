/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AssetOperationInLoadAttributeMethodAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4031";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.AssetOperationInLoadAttributeMethodDiagnosticTitle,
		messageFormat: Strings.AssetOperationInLoadAttributeMethodDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0031",
		description: Strings.AssetOperationInLoadAttributeMethodDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	private static readonly ImmutableHashSet<string> _ignoredMembers = ImmutableHashSet.Create(
		"IsAssetImportWorkerProcess",
		"IsCacheServerEnabled",
		"IsConnectedToCacheServer",
		"IsDirectoryMonitoringEnabled"
	);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{

		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMemberAccess), SyntaxKind.SimpleMemberAccessExpression);
	}

	private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
	{
		if (context.Node is not MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax ins } memberAccess)
			return;

		var text = ins.Identifier.Text;
		if (text != nameof(UnityEditor.AssetDatabase))
			return;

		var typeInfo = context
			.SemanticModel
			.GetTypeInfo(ins);

		if (typeInfo.Type == null || !typeInfo.Type.Extends(typeof(UnityEditor.AssetDatabase)))
			return;

		if (memberAccess.Name is IdentifierNameSyntax memberName && _ignoredMembers.Contains(memberName.Identifier.Text))
			return;

		if (context.ContainingSymbol is not IMethodSymbol methodSymbol)
			return;

		var typeSymbol = methodSymbol.ContainingType;

		if (IsDecoratedMethod(methodSymbol) || IsStaticCtorInDecoratedType(methodSymbol, typeSymbol))
			context.ReportDiagnostic(Diagnostic.Create(Rule, ins.GetLocation()));
	}

	private static bool IsStaticCtorInDecoratedType(IMethodSymbol methodSymbol, INamedTypeSymbol typeSymbol) => typeSymbol.StaticConstructors.Contains(methodSymbol)
																												&& InitializeOnLoadStaticCtorAnalyzer.IsDecorated(typeSymbol);

	private static bool IsDecoratedMethod(IMethodSymbol methodSymbol) => LoadAttributeMethodAnalyzer.IsDecorated(methodSymbol, onlyEditorAttributes: true);
}