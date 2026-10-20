/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: NUnit, TF rule IDs, no code fix checks.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public class InputGetKeyTests : BaseDiagnosticVerifierTest<InputGetKeyAnalyzer>
{
	[Test]
	public async Task TestMember()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKey(""a""))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 13);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestGetKeyUpLowercase()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyUp(""a""))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 13);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestGetKeyDownUppercase()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(""BACKSPACE""))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 13);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestMappedMember()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKey(""joystick 4 button 2""))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 13);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task TestTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        if (/* outer before */ Input.GetKey(/* inner before */ ""a"" /* inner after */) /* outer after */)
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 32);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}