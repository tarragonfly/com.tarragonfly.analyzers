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

public class UnusedCoroutineReturnValueTests : BaseDiagnosticVerifierTest<UnusedCoroutineReturnValueAnalyzer>
{
	[Test]
	public async Task UnusedCoroutineReturnValueTest()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

public class UnusedCoroutineScript : MonoBehaviour
{
    void Start()
    {
        UnusedCoroutine(2.0f);
    }

    private IEnumerator UnusedCoroutine(float waitTime)
    {
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForSeconds(waitTime);
        }
    }
}
";

		var diagnostic = ExpectDiagnostic(UnusedCoroutineReturnValueAnalyzer.Rule.Id)
			.WithLocation(9, 9)
			.WithArguments("UnusedCoroutine");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task UnusedCoroutineReturnValueTrivia()
	{
		const string test = @"
using System.Collections;
using UnityEngine;

public class UnusedCoroutineScript : MonoBehaviour
{
    void Start()
    {
        // leading comment
        UnusedCoroutine(2.0f); // trailing comment
    }

    private IEnumerator UnusedCoroutine(float waitTime)
    {
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForSeconds(waitTime);
        }
    }
}
";

		var diagnostic = ExpectDiagnostic(UnusedCoroutineReturnValueAnalyzer.Rule.Id)
			.WithLocation(10, 9)
			.WithArguments("UnusedCoroutine");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}