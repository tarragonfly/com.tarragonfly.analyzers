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
using Microsoft.CodeAnalysis.Operations;
using TarragonFly.Analyzers.Rules.Unity.Resources;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace TarragonFly.Analyzers.Rules.Unity;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ShaderPropertyToIDAnalyzer : ScopedAnalyzer
{
	private const string RuleId = "TF4046";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.ShaderPropertyToIDDiagnosticTitle,
		messageFormat: Strings.ShaderPropertyToIDDiagnosticMessageFormat,
		area: DiagnosticCategory.Performance,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0046",
		description: Strings.ShaderPropertyToIDDiagnosticDescription);

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(AnalyzeInvocation), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		var name = invocation.Expression switch
		{
			MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
			IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
			_ => null
		};

		if (name != "PropertyToID" || invocation.ArgumentList.Arguments.Count != 1
			|| invocation.ArgumentList.Arguments[0].Expression is not LiteralExpressionSyntax literal
			|| !literal.IsKind(SyntaxKind.StringLiteralExpression)
			|| invocation.ContainsDirectives)
			return;

		for (var parent = invocation.Parent; parent != null; parent = parent.Parent)
		{
			// Initializers already cache the computed ID.
			if (parent is BaseFieldDeclarationSyntax or EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax })
				return;

			if (parent is MemberDeclarationSyntax)
				break;
		}

		if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not IInvocationOperation operation
			|| operation.Parent is IExpressionStatementOperation)
			return;

		var method = operation.TargetMethod;
		if (!method.IsStatic || !method.ContainingType.Matches(typeof(UnityEngine.Shader))
			|| method.Parameters.Length != 1
			|| method.Parameters[0].Type.SpecialType != SpecialType.System_String
			|| method.ReturnType.SpecialType != SpecialType.System_Int32)
			return;

		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), literal.Token.ValueText));
	}
}