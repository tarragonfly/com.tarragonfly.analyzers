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

public class PhysicsAllocMethodUsageTests : BaseDiagnosticVerifierTest<PhysicsAllocMethodUsageAnalyzer>
{
	[Test]
	public async Task TestRaycastAll()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update() {
        var result = Physics.RaycastAll(Vector3.zero, Vector3.zero);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 30)
			.WithMessage("Compared to 'RaycastAll', 'RaycastNonAlloc' is not allocating memory.");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestOverlapBox()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update() {
        var result = Physics.OverlapBox(Vector3.zero, Vector3.zero);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 30)
			.WithMessage("Compared to 'OverlapBox', 'OverlapBoxNonAlloc' is not allocating memory.");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestOverlapBoxNonAlloc()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update() {
		var results = new Collider[3];
        Physics.OverlapBoxNonAlloc(Vector3.zero, Vector3.zero, results);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}
}