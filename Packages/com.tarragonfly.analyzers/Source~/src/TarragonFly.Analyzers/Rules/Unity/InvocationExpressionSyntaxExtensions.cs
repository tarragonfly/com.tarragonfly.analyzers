/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.Unity;

internal static class InvocationExpressionSyntaxExtensions
{
	public static SimpleNameSyntax? GetMethodNameSyntax(this InvocationExpressionSyntax expr)
	{
		return expr.Expression switch
		{
			MemberAccessExpressionSyntax mae => mae.Name,
			SimpleNameSyntax sns => sns,
			_ => null,
		};
	}
}