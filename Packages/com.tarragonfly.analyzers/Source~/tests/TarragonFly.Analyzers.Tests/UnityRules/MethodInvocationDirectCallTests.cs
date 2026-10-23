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

public class MethodInvocationDirectCallTests : BaseDiagnosticVerifierTest<MethodInvocationAnalyzer>
{
	[Test]
	public async Task TestStartCoroutine()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(""InvokeMe"");
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestStartCoroutineArgumentOrParenthesis()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Method(Coroutine c)
    {
        Method(((StartCoroutine(""InvokeMe""))));
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 18)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestStartCoroutineTrivia()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Start()
    {
        // comment
        StartCoroutine(/* inner */ ""InvokeMe"" /* outer */);
        /* comment */
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestStopCoroutine()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Start()
    {
        StopCoroutine(""InvokeMe"");
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}