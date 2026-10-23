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

public class DestroyTransformTests : BaseDiagnosticVerifierTest<DestroyTransformAnalyzer>
{
	[Test]
	public async Task TestValidDestroy()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() {
        Destroy(gameObject);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TestDestroy()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() {
        Destroy(transform);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestObjectDestroy()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() {
        Object.Destroy(transform, 5);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestDestroyImmediate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() {
        DestroyImmediate(transform);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestObjectDestroyImmediate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() {
        Object.DestroyImmediate(transform, true);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestDestroyTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() {
        // comment before
        Destroy(/* inline before */ transform /* inline after */ );
        // comment after
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}