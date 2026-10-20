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

public class MeshArrayPropertyInLoopTests : BaseDiagnosticVerifierTest<MeshArrayPropertyInLoopAnalyzer>
{
	[Test]
	public async Task VerticesInForLoopCondition()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            // some work
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 29)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VerticesInForLoopConditionNoVariable()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        for (int i = 0; i < GetComponent<MeshFilter>().mesh.vertices.Length; i++)
        {
            // some work
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 29)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task VerticesInForLoopBody()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < 10; i++)
        {
            var v = mesh.vertices[i];
            var x = mesh.vertices[i] + mesh.vertices[i];
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(11, 21)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VerticesInForLoopConditionAndBody()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            var v = mesh.vertices[i];
            var x = mesh.vertices[i] + mesh.vertices[i];
            var z = 2 * mesh.vertices.Length + mesh.vertices.Length;
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 29)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task NormalsInWhileLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        int i = 0;
        while (i < mesh.normals.Length)
        {
            i++;
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 20)
			.WithArguments("normals");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ColorsInForEachLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        foreach (var c in mesh.colors)
        {
            // some work
        }
    }
}
";

		// Note: foreach evaluates the collection expression only once at the start,
		// so this is actually not inside the loop body that repeats.
		// We should NOT report this case.
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task VerticesOutsideLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        var vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            // some work
        }
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task MultiplePropertiesInLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            mesh.vertices[i] += mesh.normals[i] * Time.deltaTime;
        }
    }
}
";

		var diagnostic1 = ExpectDiagnostic()
			.WithLocation(11, 13)
			.WithArguments("vertices");

		var diagnostic2 = ExpectDiagnostic()
			.WithLocation(11, 33)
			.WithArguments("normals");

		await VerifyCSharpDiagnosticAsync(test, diagnostic1, diagnostic2);
	}

	[Test]
	public async Task UvPropertyInLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < mesh.uv.Length; i++)
        {
            // some work
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 29)
			.WithArguments("uv");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TangentsPropertyInLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < mesh.tangents.Length; i++)
        {
            // some work
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 29)
			.WithArguments("tangents");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DoWhileLoop()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh meshLocal = GetComponent<MeshFilter>().mesh;
        int i = 0;
        do
        {
            var v = meshLocal.vertices[i];
            i++;
        } while (i < 10);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(12, 21)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DoWhileLoopCondition()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        int i = 0;
        do
        {
            i++;
        } while (i < mesh.vertices.Length);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(13, 22)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task VertexCountNotReported()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            // some work - using vertexCount is fine, no allocation
        }
    }
}
";

		// vertexCount is not an allocating property
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task VariableNameConflict()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        var meshVertices = new Vector3[0]; // already declared
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            // some work
        }
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 29)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task LocalFunctionDoesNotCrossLoopBoundary()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < 10; i++)
        {
            LocalFunction();
        }

        void LocalFunction()
        {
            // This is not inside the loop body from analyzer's perspective
            var v = mesh.vertices;
        }
    }
}
";

		// The local function is not considered inside the loop
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task LambdaDoesNotCrossLoopBoundary()
	{
		const string test = @"
using UnityEngine;
using System;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        for (int i = 0; i < 10; i++)
        {
            Action a = () => {
                // This is not inside the loop body from analyzer's perspective
                var v = mesh.vertices;
            };
        }
    }
}
";

		// The lambda is not considered inside the loop
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task VerticesInForLoopConditionTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Mesh mesh = GetComponent<MeshFilter>().mesh;
        // loop start
        for (int i = 0; i < /* vertices access */ mesh.vertices.Length /* end access */; i++)
        {
            // some work
        }
        // loop end
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 51)
			.WithArguments("vertices");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}