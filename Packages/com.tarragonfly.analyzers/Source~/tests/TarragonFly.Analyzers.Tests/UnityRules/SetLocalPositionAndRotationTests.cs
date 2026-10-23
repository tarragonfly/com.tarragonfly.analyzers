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

public class SetLocalPositionAndRotationTests : BaseDiagnosticVerifierTest<SetLocalPositionAndRotationAnalyzer>
{
	// For extensive testing, see SetPositionAndRotationTests.cs
	[Test]
	public async Task UpdateLocalPositionAndRotationMethod()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        transform.localPosition = new Vector3(0.0f, 1.0f, 0.0f);
        transform.localRotation = transform.localRotation;
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Transform);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(8, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task UpdateLocalPositionAndRotationMethodTransformAccess()
	{
		const string test = @"
using UnityEngine;
using UnityEngine.Jobs;

class Context
{
    void Method()
    {
        var stub = new TransformAccess();
        stub.localPosition = new Vector3(0.0f, 1.0f, 0.0f);
        stub.localRotation = stub.localRotation;
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
	public async Task UpdateLocalPositionAndRotationMethodTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        // leading comment
        transform.localPosition = new Vector3(0.0f, 1.0f, 0.0f);
        transform.localRotation = transform.localRotation;
        // trailing comment
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Transform);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(9, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}