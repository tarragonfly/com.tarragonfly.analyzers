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

public class EmptyUnityMessageTests : BaseDiagnosticVerifierTest<EmptyUnityMessageAnalyzer>
{
	[Test]
	public async Task EmptyFixedUpdateTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    // comment expected to be removed
    private void FixedUpdate()
    {
    }

    private void Foo()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("FixedUpdate");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task AwaitableEmptyFixedUpdate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    // comment expected to be removed
    private async Awaitable FixedUpdate()
    {
    }

    private void Foo()
    {
    }
}
";
		var context = AnalyzerVerificationContext
			.Default
			.WithAnalyzerFilter("CS1998");

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 29)
			.WithArguments("FixedUpdate");

		await VerifyCSharpDiagnosticAsync(context, test, diagnostic);
	}

	[Test]
	public async Task FixedUpdateWithBody()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void FixedUpdate()
    {
        Debug.Log(nameof(FixedUpdate));
    }

    private void Foo()
    {
    }
}
";
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task VirtualFixedUpdate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public virtual void FixedUpdate()
    {
    }
}
";
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task VirtualOverrideFixedUpdate()
	{
		const string test = @"
using UnityEngine;

class BaseBehaviour : MonoBehaviour
{
	public virtual void FixedUpdate()
	{
	}
}

class Camera : BaseBehaviour
{
    public override void FixedUpdate()
    {
    }
}
";
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task AbstractOverrideFixedUpdate()
	{
		const string test = @"
using UnityEngine;

abstract class BaseBehaviour : MonoBehaviour
{
	public abstract void FixedUpdate();
}

class Camera : BaseBehaviour
{
    public override void FixedUpdate()
    {
    }
}
";
		await VerifyCSharpDiagnosticAsync(test);
	}
}