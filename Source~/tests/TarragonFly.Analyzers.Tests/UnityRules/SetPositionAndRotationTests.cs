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

public class SetPositionAndRotationTests : BaseDiagnosticVerifierTest<SetPositionAndRotationAnalyzer>
{
	[Test]
	public async Task UpdatePositionAndRotationMethod()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        transform.position = new Vector3(0.0f, 1.0f, 0.0f);
        transform.rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task UpdatePositionAndRotationMethodTransformAccess()
	{
		const string test = @"
using UnityEngine;
using UnityEngine.Jobs;

class Context
{
    void Method()
    {
        var stub = new TransformAccess();
        stub.position = new Vector3(0.0f, 1.0f, 0.0f);
        stub.rotation = stub.rotation;
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Jobs.TransformAccess);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(10, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task UpdatePositionAndRotationMethodTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        // position
        transform.position = /* inner position */ new Vector3(0.0f, 1.0f, 0.0f) /* outer position */;
        // rotation
        transform.rotation = /* inner rotation */ transform.rotation /* outer rotation */;
        // trailing
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(9, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task MultiplePositionChanges()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        transform.position = new Vector3(0.0f, 1.0f, 0.0f);
        transform.position = new Vector3(0.5f, 1.0f, 2.0f);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task SeparateBlocks()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Start()
    {
        transform.position = new Vector3(0.0f, 1.0f, 0.0f);
    }

    void Update()
    {
        transform.rotation = transform.rotation;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task PositionVariableUpdate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 newPosition = new Vector3(1,2,3);
        transform.rotation = transform.rotation;
        transform.position = newPosition;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(9, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task MemberExpression()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        GameObject instance = null;
        GameObject go = null;
        instance.transform.position = go.transform.position;
        instance.transform.rotation = go.transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(10, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DistinctMemberExpression()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        GameObject instance = null;
        GameObject go = null;
        instance.transform.position = go.transform.position;
        go.transform.rotation = go.transform.rotation;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ImplicitConversion()
	{
		const string test = @"
using UnityEngine;

struct float3
{
    public static implicit operator Vector3(float3 v) { return Vector3.up; }
    public static implicit operator float3(Vector3 v) { return new float3(); }
}


class Camera : MonoBehaviour
{
    void Update()
    {
        float3 position;

        transform.position = position;
        transform.rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(17, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RotationReusingPosition()
	{
		// https://github.com/microsoft/Microsoft.Unity.Analyzers/issues/428

		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        var Target = GameObject.Find(""Sphere"").transform;
        transform.position = Target.position + GetComputedOffset();
        transform.rotation = Quaternion.LookRotation(Target.position - transform.position, Vector3.up);
    }

    private Vector3 GetComputedOffset()
    {
        return Vector3.up + Vector3.left;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task RotationReusingPositionWithThisSemantic()
	{
		// Test semantic equivalence in expression reuse detection
		// transform.position in line 1 should be detected as reused in line 2 even with this. prefix

		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        var Target = GameObject.Find(""Sphere"").transform;
        this.transform.position = Target.position + GetComputedOffset();
        transform.rotation = Quaternion.LookRotation(Target.position - transform.position, Vector3.up);
    }

    private Vector3 GetComputedOffset()
    {
        return Vector3.up + Vector3.left;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task RotationReusingPositionWithVariable()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        var Target = GameObject.Find(""Sphere"").transform;
        var newPosition = Target.position + GetComputedOffset();
        transform.position = newPosition;
        transform.rotation = Quaternion.LookRotation(Target.position - newPosition, Vector3.up);
    }

    private Vector3 GetComputedOffset()
    {
        return Vector3.up + Vector3.left;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(10, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task SemanticEquivalenceWithThis()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        this.transform.position = new Vector3(0.0f, 1.0f, 0.0f);
        transform.rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task SemanticEquivalenceReverseWithThis()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        transform.position = new Vector3(0.0f, 1.0f, 0.0f);
        this.transform.rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}