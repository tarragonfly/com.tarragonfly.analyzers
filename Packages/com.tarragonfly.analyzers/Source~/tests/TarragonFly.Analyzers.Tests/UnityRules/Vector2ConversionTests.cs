/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: NUnit, TF rule IDs, no code fix checks.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public class Vector2ConversionTests : BaseDiagnosticVerifierTest<Vector2ConversionAnalyzer>
{
	[Test]
	public async Task UseConversionTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 v3 = Vector3.zero;
        Vector2 v2 = Vector2.zero;
        var distance = Vector3.Distance(v3, new Vector3(v2.x, v2.y, 0));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 45);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DoNotUseConversionWithoutSameIdentifierTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 v3 = Vector3.zero;
        Vector2 v2 = Vector2.zero;
        Vector2 v2b = Vector2.zero;
        var distance = Vector3.Distance(v3, new Vector3(v2.x, v2b.y, 0));
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task DoNotUseConversionWithoutNonZeroZTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 v3 = Vector3.zero;
        Vector2 v2 = Vector2.zero;
        var distance = Vector3.Distance(v3, new Vector3(v2.x, v2.y, 1));
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task NoAmbiguityAfterConversionTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 v3 = Vector3.zero;
        Vector2 v2 = Vector2.zero;
        var test = new Vector3(v2.x, v2.y, 0);
        var result = v3 - test;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 20);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task NoAmbiguousMethodCallTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Foo(Vector3 bar) {
    }

    void Foo(Vector2 bar) {
    }

    void Update()
    {
        Vector2 v2 = Vector2.zero;
        Foo(new Vector3(v2.x, v2.y, 0));
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task UseConversionTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 v3 = Vector3.zero;
        Vector2 v2 = Vector2.zero;
        // leading comment
        var distance = Vector3.Distance(v3, /* inner */ new Vector3(v2.x, v2.y, 0) /* outer */);
        // trailing comment
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(11, 57);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}