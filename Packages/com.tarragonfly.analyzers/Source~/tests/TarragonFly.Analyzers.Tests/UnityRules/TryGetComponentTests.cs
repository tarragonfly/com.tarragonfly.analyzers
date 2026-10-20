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

public class TryGetComponentTests : BaseDiagnosticVerifierTest<TryGetComponentAnalyzer>
{
	[Test]
	public async Task VariableDeclarationNotNullConditionTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        var rb = gameObject.GetComponent<Rigidbody>();
        if (rb != null) {
            Debug.Log(rb.name);
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VariableDeclarationScopeTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        if (true) {
            var rb = gameObject.GetComponent<Rigidbody>();
            if (rb != null) {
                Debug.Log(rb.name);
            }
            if (rb == null) {
                Debug.Log(""null"");
            }
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 22);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VariableDeclarationTriviaTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        // before assignment
        var rb = gameObject.GetComponent<Rigidbody>();
        // after assignment, before if
        if (rb != null) {
            Debug.Log(rb.name);
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task VariableDeclarationNotNullConditionNoMemberAccessOnComponent()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        var rb = GetComponent<Rigidbody>();
        if (rb != null) {
            Debug.Log(rb.name);
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VariableDeclarationNotNullConditionReverseOperandsTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        var rb = gameObject.GetComponent<Rigidbody>();
        if (null != rb) {
            Debug.Log(rb.name);
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VariableDeclarationNullConditionTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        var rb = gameObject.GetComponent<Rigidbody>();
        if (rb == null) {
            Debug.Log(""null!"");
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task PropertyAssignmentNotNullConditionTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private Rigidbody rb { get; set;}

    public void Update() 
    {
        rb = gameObject.GetComponent<Rigidbody>();
        if (rb != null) {
            Debug.Log(rb.name);
        }
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task MismatchAssignmentNotNullConditionTest()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        Rigidbody rb1, rb2;
        rb2 = null;
        rb1 = gameObject.GetComponent<Rigidbody>();
        if (rb2 != null) {
            Debug.Log(rb2.name);
        }
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task NoGetComponentDerivatives()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        var rb = gameObject.GetComponentInChildren<Rigidbody>();
        if (rb != null) {
            Debug.Log(rb.name);
        }
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task BlockBreaksDetection()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        Component hit = null, platform = null;

        if (hit != null) {
            platform = hit.GetComponent<Component>();
        }
        if (platform != null)
            transform.parent = platform.transform;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task InlineIfWithElseClauseBreaksDetection()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update() 
    {
        Component hit = null, platform = null, foo = null;

        if (hit != null)
            platform = hit.GetComponent<Component>();
        else
            foo = foo ?? null;
        if (platform != null)
            transform.parent = platform.transform;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}
}