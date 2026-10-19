/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.Unity;

internal static class MethodDeclarationExtensions
{
	public static bool HasPolymorphicModifier(this MethodDeclarationSyntax method)
	{
		foreach (var modifier in method.Modifiers)
		{
			switch (modifier.Kind())
			{
				case SyntaxKind.AbstractKeyword:
				case SyntaxKind.VirtualKeyword:
				case SyntaxKind.OverrideKeyword:
					return true;
			}
		}

		return false;
	}
}