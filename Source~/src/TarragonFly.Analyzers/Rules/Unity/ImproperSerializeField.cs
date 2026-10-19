/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Generic;
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
public class ImproperSerializeFieldAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4013";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.ImproperSerializeFieldDiagnosticTitle,
		messageFormat: Strings.ImproperSerializeFieldDiagnosticMessageFormat,
		area: DiagnosticCategory.Correctness,
		severity: DiagnosticSeverity.Info,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0013",
		description: Strings.ImproperSerializeFieldDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMemberDeclaration), SyntaxKind.PropertyDeclaration);
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeMemberDeclaration), SyntaxKind.FieldDeclaration);
	}

	private static void AnalyzeMemberDeclaration(SyntaxNodeAnalysisContext context)
	{
		var model = context.SemanticModel;
		ISymbol? symbol;

		switch (context.Node)
		{
			case PropertyDeclarationSyntax pdec:
				symbol = model.GetDeclaredSymbol(pdec);
				break;
			case FieldDeclarationSyntax fdec:
				if (fdec.Declaration.Variables.Count == 0)
					return;

				// attributes are applied to all fields declaration symbols
				// just get the first one
				symbol = model.GetDeclaredSymbol(fdec.Declaration.Variables[0]);
				break;
			default:
				// we only support field/property analysis
				return;
		}

		if (symbol == null)
			return;

		if (!IsReportable(symbol))
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, context.Node.GetLocation(), symbol.Name));
	}

	private static bool IsReportable(ISymbol symbol)
	{
		if (!symbol.ContainingType.Extends(typeof(UnityEngine.Object)))
			return false;

		if (!symbol.GetAttributes().Any(a => a.AttributeClass != null && a.AttributeClass.Matches(typeof(UnityEngine.SerializeField))))
			return false;

		return symbol switch
		{
			IFieldSymbol fieldSymbol => fieldSymbol.IsStatic || fieldSymbol.IsReadOnly || IsRedundantOnPublicField(fieldSymbol), // redundant on non-dictionary public fields and invalid on static/readonly fields
			IPropertySymbol => true, // Should never be on a property
			_ => false,
		};
	}

	private static bool IsRedundantOnPublicField(IFieldSymbol fieldSymbol)
	{
		return fieldSymbol.DeclaredAccessibility == Accessibility.Public
			   && !fieldSymbol.Type.Extends(typeof(Dictionary<,>));
	}
}