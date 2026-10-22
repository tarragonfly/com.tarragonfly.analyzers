/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for tarragon.fly Analyzers: NUnit, TF rule IDs, no code fix checks, the wider scope.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public class PreferGenericOverloadTests : BaseDiagnosticVerifierTest<PreferGenericOverloadAnalyzer>
{
	[Test]
	public async Task GetComponentAs()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
	private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent(typeof(Rigidbody)) as Rigidbody;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 14)
			.WithArguments("GetComponent(typeof(Rigidbody)) has a generic overload", "GetComponent<Rigidbody>()");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentAsArgumentOrParenthesis()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Method(Rigidbody rb)
    {
        Method(((GetComponent(typeof(Rigidbody)) as Rigidbody)));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18)
			.WithArguments("GetComponent(typeof(Rigidbody)) has a generic overload", "GetComponent<Rigidbody>()");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentAsTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
	private Rigidbody rb;

    private void Start()
    {
        // comment
        rb = /* inner */ GetComponent(typeof(Rigidbody)) as Rigidbody;
        /* comment */
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(11, 26)
			.WithArguments("GetComponent(typeof(Rigidbody)) has a generic overload", "GetComponent<Rigidbody>()");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task CastGetComponent()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
	private Rigidbody rb;

    private void Start()
    {
        rb = (Rigidbody)GetComponent(typeof(Rigidbody));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 25)
			.WithArguments("GetComponent(typeof(Rigidbody)) has a generic overload", "GetComponent<Rigidbody>()");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentsInChildrenBoolean()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        GetComponentsInChildren(typeof(Rigidbody), true);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9)
			.WithArguments("GetComponentsInChildren(typeof(Rigidbody)) has a generic overload", "GetComponentsInChildren<Rigidbody>(...)");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentTypeVariable()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        var t = typeof(Rigidbody);
        GetComponent(t);
    }
}
";

		// We're assuming that using GetComponent with anything else than a typeof
		// argument is from a computation that makes it impossible to use the generic form
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task GetComponentGeneric()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        GetComponent<Rigidbody>();
    }
}
";

		// Verify we're not misreporting an already generic usage
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TryGetComponent()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Start()
    {
        if (!TryGetComponent(typeof(Rigidbody), out var sb))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 14)
			.WithArguments("TryGetComponent(typeof(Rigidbody)) has a generic overload", "TryGetComponent<Rigidbody>(...)");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task GetComponentOnAnotherObject()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        var rb = other.GetComponent(typeof(Rigidbody));
        var body = other?.GetComponent(typeof(Rigidbody));
    }
}
";

		await VerifyCSharpDiagnosticAsync(test,
			ExpectDiagnostic().WithLocation(8, 18).WithArguments("GetComponent(typeof(Rigidbody)) has a generic overload", "GetComponent<Rigidbody>()"),
			ExpectDiagnostic().WithLocation(9, 26).WithArguments("GetComponent(typeof(Rigidbody)) has a generic overload", "GetComponent<Rigidbody>()"));
	}

	[Test]
	public async Task AddComponentAndCreateInstanceWithTypeOf()
	{
		const string test = @"
using UnityEngine;

class Settings : ScriptableObject { }

class Spawner : MonoBehaviour
{
    private void Start()
    {
        gameObject.AddComponent(typeof(Rigidbody));
        ScriptableObject.CreateInstance(typeof(Settings));
        gameObject.AddComponent(typeof(Settings));
    }
}
";

		// AddComponent<Settings>() wouldn't compile, so the last call has no generic form to suggest.
		await VerifyCSharpDiagnosticAsync(test,
			ExpectDiagnostic().WithLocation(10, 9).WithArguments("AddComponent(typeof(Rigidbody)) has a generic overload", "AddComponent<Rigidbody>()"),
			ExpectDiagnostic().WithLocation(11, 9).WithArguments("CreateInstance(typeof(Settings)) has a generic overload", "CreateInstance<Settings>()"));
	}

	[Test]
	public async Task TypeNameInAString()
	{
		const string test = @"
using UnityEngine;

namespace Game.Data
{
    class Settings : ScriptableObject { }
}

class Spawner : MonoBehaviour
{
    private const string BodyName = ""Rigidbody"";

    private void Start(string fromData)
    {
        GetComponent(BodyName);
        GetComponent(""Rigidbdy"");
        ScriptableObject.CreateInstance(""Game.Data.Settings"");
        ScriptableObject.CreateInstance(""Setings"");
        GetComponent(fromData);
    }
}
";

		await VerifyCSharpDiagnosticAsync(test,
			ExpectDiagnostic().WithLocation(15, 9).WithArguments("GetComponent(\"Rigidbody\") looks the type up by name when the game runs", "GetComponent<Rigidbody>()"),
			ExpectDiagnostic().WithLocation(16, 9).WithArguments("GetComponent(\"Rigidbdy\") names no component type, so it returns null", "the generic overload"),
			ExpectDiagnostic().WithLocation(17, 9).WithArguments("CreateInstance(\"Game.Data.Settings\") looks the type up by name when the game runs", "CreateInstance<Game.Data.Settings>()"),
			ExpectDiagnostic().WithLocation(18, 9).WithArguments("CreateInstance(\"Setings\") names no ScriptableObject type, so it returns null", "the generic overload"));
	}
}