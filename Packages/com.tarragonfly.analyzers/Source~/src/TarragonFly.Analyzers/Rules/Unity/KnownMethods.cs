/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.Unity;

internal static class KnownMethods
{
	internal static readonly HashSet<string> GetComponentNames =
	[
		nameof(UnityEngine.Component.GetComponent),
		nameof(UnityEngine.Component.GetComponentInChildren),
		nameof(UnityEngine.Component.GetComponentInParent),
		nameof(UnityEngine.Component.GetComponents),
		nameof(UnityEngine.Component.GetComponentsInChildren),
		nameof(UnityEngine.Component.GetComponentsInParent),
		nameof(UnityEngine.Component.TryGetComponent),
	];

	// Be careful when using those helper methods, as GetComponentNames contains methods with different return types (Component, array or bool)
	public static bool IsGetComponentName(SimpleNameSyntax name) => GetComponentNames.Contains(name.Identifier.Text);

	public static bool IsGetComponent(IMethodSymbol method)
	{
		if (!GetComponentNames.Contains(method.Name))
			return false;

		var containingType = method.ContainingType;
		return containingType.Matches(typeof(UnityEngine.Component)) || containingType.Matches(typeof(UnityEngine.GameObject));
	}
}