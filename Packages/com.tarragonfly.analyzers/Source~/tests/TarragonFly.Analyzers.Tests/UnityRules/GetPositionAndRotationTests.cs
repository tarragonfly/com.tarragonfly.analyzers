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

public class GetPositionAndRotationTests : BaseDiagnosticVerifierTest<GetPositionAndRotationAnalyzer>
{
	[Test]
	public async Task UseGetPositionAndRotationMethod()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 position;
        Quaternion rotation;
        position = transform.position;
        rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(10, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task UseGetPositionAndRotationMethodTransformAccess()
	{
		const string test = @"
using UnityEngine;
using UnityEngine.Jobs;

class Context
{
    void Method()
    {
        Vector3 position;
        Quaternion rotation;
        var stub = new TransformAccess();
        position = stub.position;
        rotation = stub.rotation;
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Jobs.TransformAccess);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(12, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task OutRefCompatibility()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        transform.position = transform.position;
        transform.rotation = transform.rotation;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ImplicitVariableDeclaration()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        var position = transform.position;
        var rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ExplicitVariableDeclaration()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ImplicitConversionExplicitVariableDeclaration()
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
        float3 position = transform.position;
        Quaternion rotation = transform.rotation;
    }
}
";

		// Implicit conversion incompatible with 'out'
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ImplicitConversionAssignment()
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
        Quaternion rotation;

        position = transform.position;
        rotation = transform.rotation;
    }
}
";

		// Implicit conversion incompatible with 'out'
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task UseGetPositionAndRotationMethodAssignmentTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Vector3 position;
        Quaternion rotation;
        // position comment
        position = transform.position;
        // rotation comment
        rotation = transform.rotation;
        // trailing comment
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(11, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}