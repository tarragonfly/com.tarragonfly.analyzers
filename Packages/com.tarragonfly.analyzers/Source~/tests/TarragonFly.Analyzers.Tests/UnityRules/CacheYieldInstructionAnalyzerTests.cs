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

public class CacheYieldInstructionAnalyzerTests : BaseDiagnosticVerifierTest<CacheYieldInstructionAnalyzer>
{
	[Test]
	public async Task WaitForSecondsStatic()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

class Camera : MonoBehaviour
{
    static IEnumerator Coroutine()
    {
        yield return new WaitForSeconds(1.666f);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 22);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task WaitForSecondsStaticTrivia()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

class Camera : MonoBehaviour
{
    // before
    private int foo = 0; // inline
    // after

    static IEnumerator Coroutine()
    {
        // before
        yield return new WaitForSeconds(1.666f); // inline
        // after
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(14, 22);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task WaitForSecondsOnlyLiteral()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

class Camera : MonoBehaviour
{
    IEnumerator Coroutine()
    {
        float a = 1f;
        yield return new WaitForSeconds(a);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task WaitForSecondsMultiple()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

class Camera : MonoBehaviour
{
    IEnumerator Coroutine1()
    {
        yield return new WaitForSeconds(1f);
    }

    IEnumerator Coroutine2()
    {
        yield return new WaitForSeconds(2f);
    }
}
";

		var diagnostic1 = ExpectDiagnostic()
			.WithLocation(9, 22);

		var diagnostic2 = ExpectDiagnostic()
			.WithLocation(14, 22);

		await VerifyCSharpDiagnosticAsync(test, diagnostic1, diagnostic2);
	}

	[Test]
	public async Task WaitForSecondsReuse()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

class Camera : MonoBehaviour
{
    IEnumerator Coroutine1()
    {
        yield return new WaitForSeconds(1f);
    }

    IEnumerator Coroutine2()
    {
        yield return new WaitForSeconds(1f);
    }
}
";

		var diagnostic1 = ExpectDiagnostic()
			.WithLocation(9, 22);

		var diagnostic2 = ExpectDiagnostic()
			.WithLocation(14, 22);

		await VerifyCSharpDiagnosticAsync(test, diagnostic1, diagnostic2);
	}

	[Test]
	public async Task NonUnityTypeWithMultipleLiteralArguments()
	{
		// See https://github.com/microsoft/Microsoft.Unity.Analyzers/issues/471
		// The analyzer used to throw AD0001 (InvalidOperationException: Sequence contains more than one matching element)
		// when a yield return expression created an object with more than one literal argument.
		const string test = @"
using System.Collections.Generic;

public struct A
{
    public A(int a, int b)
    {
    }

    public static IEnumerable<A> GetObjs()
    {
        yield return new A(23, 23);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task WaitForSecondsAndRealtime()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

class Camera : MonoBehaviour
{
    IEnumerator Coroutine1()
    {
        yield return new WaitForSeconds(1f);
    }

    IEnumerator Coroutine2()
    {
        yield return new WaitForSecondsRealtime(1f);
    }
}
";

		var diagnostic1 = ExpectDiagnostic()
			.WithLocation(9, 22);

		await VerifyCSharpDiagnosticAsync(test, diagnostic1);
	}
}
