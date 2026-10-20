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

public class UpdateWithoutDeltaTimeTests : BaseDiagnosticVerifierTest<UpdateDeltaTimeAnalyzer>
{
	[Test]
	public async Task FixedUpdateWithDeltaTime()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
     public void FixedUpdate()
     {
         var foo = Time.deltaTime;
     }
}
";
		// see https://github.com/microsoft/Microsoft.Unity.Analyzers/issues/26
		// this rule is now disabled by default
		// but can be re-enabled using ruleset or editorconfig:
		// dotnet_diagnostic.UNT0005.severity = suggestion

		var diagnostic = ExpectDiagnostic(UpdateDeltaTimeAnalyzer.FixedUpdateId)
			.WithLocation(8, 25);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task FixedUpdateWithDeltaTimeTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
     public void FixedUpdate()
     {
         // comment
         var foo = /* inner */ Time.deltaTime /* outer */;
         /* comment */
     }
}
";
		// see https://github.com/microsoft/Microsoft.Unity.Analyzers/issues/26
		// this rule is now disabled by default
		// but can be re-enabled using ruleset or editorconfig:
		// dotnet_diagnostic.UNT0005.severity = suggestion

		var diagnostic = ExpectDiagnostic(UpdateDeltaTimeAnalyzer.FixedUpdateId)
			.WithLocation(9, 37);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task UpdateWithFixedDeltaTime()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
     public void Update()
     {
         var foo = Time.fixedDeltaTime;
     }
}
";

		var diagnostic = ExpectDiagnostic(UpdateDeltaTimeAnalyzer.UpdateId)
			.WithLocation(8, 25);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}