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

public class NonGenericGetComponentTests : BaseDiagnosticVerifierTest<NonGenericGetComponentAnalyzer>
{
	[Test]
	public async Task GetComponentAs()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
	private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent(typeof(Rigidbody)) as Rigidbody;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 14)
			.WithArguments("GetComponent");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentAsArgumentOrParenthesis()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Method(Rigidbody rb)
    {
        Method(((GetComponent(typeof(Rigidbody)) as Rigidbody)));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18)
			.WithArguments("GetComponent");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentAsTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
	private Rigidbody rb;

    private void Start()
    {
        // comment
        rb = /* inner */ GetComponent(typeof(Rigidbody)) as Rigidbody;
        /* comment */
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(11, 26)
			.WithArguments("GetComponent");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task CastGetComponent()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
	private Rigidbody rb;

    private void Start()
    {
        rb = (Rigidbody)GetComponent(typeof(Rigidbody));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 25)
			.WithArguments("GetComponent");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentsInChildrenBoolean()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        GetComponentsInChildren(typeof(Rigidbody), true);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9)
			.WithArguments("GetComponentsInChildren");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentTypeVariable()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        var t = typeof(Rigidbody);
        GetComponent(t);
    }
}
";

		// We're assuming that using GetComponent with anything else than a typeof
		// argument is from a computation that makes it impossible to use the generic form
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task GetComponentGeneric()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        GetComponent<Rigidbody>();
    }
}
";

		// Verify we're not misreporting an already generic usage
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TryGetComponent()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        if (!TryGetComponent(typeof(Rigidbody), out var sb))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 14)
			.WithArguments("TryGetComponent");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}