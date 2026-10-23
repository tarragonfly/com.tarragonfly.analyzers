/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: namespace, NUnit.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;

namespace TarragonFly.Analyzers.Tests.UnityRules;

[Flags]
public enum DiagnosticLocationOptions
{
	None = 0,
	IgnoreLength = 1
}