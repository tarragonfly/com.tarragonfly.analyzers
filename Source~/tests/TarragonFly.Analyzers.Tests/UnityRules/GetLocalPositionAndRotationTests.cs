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

public class GetLocalPositionAndRotationTests : BaseDiagnosticVerifierTest<GetLocalPositionAndRotationAnalyzer>
{
	// For extensive testing, see GetPositionAndRotationTests
	[Test]
	public async Task UseGetLocalPositionAndRotationMethod()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Quaternion rotation;
        rotation = transform.localRotation;
        var position = transform.localPosition;
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Transform);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(9, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task UseGetLocalPositionAndRotationMethodTransformAccess()
	{
		const string test = @"
using UnityEngine.Jobs;

class Context
{
    void Method()
    {
        var stub = new TransformAccess();
        var foo = stub.localPosition;
        var bar = stub.localRotation;
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Jobs.TransformAccess);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(9, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task NoTypeMismatch()
	{
		const string test = @"
using UnityEngine;
using UnityEngine.Jobs;

class Camera : MonoBehaviour
{
    void Update()
    {
        var stub = new TransformAccess();
        var foo = stub.localPosition;
        var bar = transform.localRotation;
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task UseGetLocalPositionAndRotationMethodTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        Quaternion rotation;
        // rotation comment
        rotation = transform.localRotation;
        // position comment
        var position = transform.localPosition;
        // trailing comment
    }
}
";

		var method = GetCSharpDiagnosticAnalyzer().ExpressionContext.PositionAndRotationMethodName;
		var type = typeof(UnityEngine.Transform);

		SkipUnless(MethodExists("UnityEngine", type.FullName!, method), $"This Unity version does not support {type}.{method}");

		var diagnostic = ExpectDiagnostic().WithLocation(10, 9);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}