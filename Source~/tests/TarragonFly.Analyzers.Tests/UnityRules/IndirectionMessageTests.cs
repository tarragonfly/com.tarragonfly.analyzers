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

public class IndirectionMessageTests : BaseDiagnosticVerifierTest<IndirectionMessageAnalyzer>
{
	[Test]
	public async Task RemoveGameObjectProperty()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void OnTriggerEnter(Collider collider)
    {
        GameObject original = null;
        GameObject duplicate = original.gameObject;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(9, 32);
		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RemoveGameObjectPropertyArgumentOrParenthesis()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Method(GameObject argument)
    {
        GameObject original = null;
        Method(((original.gameObject)));
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(9, 18);
		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RemoveGameObjectPropertyTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void OnTriggerEnter(Collider collider)
    {
        GameObject original = null;
        // comment
        GameObject duplicate = /* inner */ original.gameObject;
        /* comment */
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(10, 44);
		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RemoveGameObjectPropertyMultiple()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void OnTriggerEnter(Collider collider)
    {
        GameObject original = null;
        GameObject duplicate = original.gameObject.gameObject;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(9, 32);
		await VerifyCSharpDiagnosticAsync(test, diagnostic, diagnostic);
	}

	[Test]
	public async Task RemoveGameObjectPropertyMethod()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void OnTriggerEnter(Collider collider)
    {
        GameObject duplicate = GetGameObject().gameObject;
    }

    GameObject GetGameObject()
    {
        return null;
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 32);
		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}