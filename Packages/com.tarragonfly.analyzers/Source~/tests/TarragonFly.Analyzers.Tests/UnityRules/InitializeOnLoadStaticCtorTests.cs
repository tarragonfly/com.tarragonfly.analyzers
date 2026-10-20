/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: NUnit, TF rule IDs, no code fix checks.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public class InitializeOnLoadStaticCtorTests : BaseDiagnosticVerifierTest<InitializeOnLoadStaticCtorAnalyzer>
{
	[Test]
	public async Task InitializeOnLoadWithoutStaticCtor()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
class Camera : MonoBehaviour
{
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 7)
			.WithArguments("Camera");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task InitializeOnLoadWithImplicitStaticCtor()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public sealed class Camera : MonoBehaviour
{
    public static readonly int willGenerateImplicitStaticCtor = 666;
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 21)
			.WithArguments("Camera");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task InitializeOnLoadWithoutStaticCtorTrivia()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
class Camera : MonoBehaviour
{
    // comment
    private int field = 0;
    /* comment */
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 7)
			.WithArguments("Camera");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}