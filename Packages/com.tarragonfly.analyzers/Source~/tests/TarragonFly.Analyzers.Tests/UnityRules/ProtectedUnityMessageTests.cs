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

public class ProtectedUnityMessageTests : BaseDiagnosticVerifierTest<ProtectedUnityMessageAnalyzer>
{
	[Test]
	public async Task FixPrivateUnityMessage()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
    }

    private void Foo()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(6, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task FixDefaultUnityMessageTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    // comment with a space
    void Start() /* comment with space */
    {
    }

    void Foo()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(7, 10);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task FixPrivateUnityMessageTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    // comment
    private void Start() /* comment */
    {
    }

    private void Foo()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(7, 18);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task FixPublicWithModifiersUnityMessage()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public virtual void Awake()
    {
    }

    protected void Update()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(6, 25);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task FixModifiersUnityMessage()
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

		var diagnostic = ExpectDiagnostic().WithLocation(6, 25);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task FixUnityMessageWithBody()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    public void Update()
    {
        Vector3 position = new Vector3(1,1,1);
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(6, 17);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task SealedClassUnityMessage()
	{
		const string test = @"
using UnityEngine;

public sealed class Camera : MonoBehaviour
{
    public void Update()
    {
        Vector3 position = new Vector3(1,1,1);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task SealedPartialClassUnityMessage()
	{
		const string test = @"
using UnityEngine;

partial class Camera : MonoBehaviour
{
    public void Update()
    {
        Vector3 position = new Vector3(1,1,1);
    }
}

sealed partial class Camera
{
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ShouldNotWarnForOverride()
	{
		const string test = @"
using UnityEngine;

class StateMachine : StateMachineBehaviour
{
    public override void OnStateEnter(Animator animator, AnimatorStateInfo animatorStateInfo, int layerIndex)
    {
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}
}