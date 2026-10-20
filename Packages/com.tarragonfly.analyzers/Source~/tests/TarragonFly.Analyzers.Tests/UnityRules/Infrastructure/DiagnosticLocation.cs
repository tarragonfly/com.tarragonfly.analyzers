/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: namespace, NUnit.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using Microsoft.CodeAnalysis;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public readonly struct DiagnosticLocation(FileLinePositionSpan span, DiagnosticLocationOptions options)
{
	public FileLinePositionSpan Span { get; } = span;

	public DiagnosticLocationOptions Options { get; } = options;
}