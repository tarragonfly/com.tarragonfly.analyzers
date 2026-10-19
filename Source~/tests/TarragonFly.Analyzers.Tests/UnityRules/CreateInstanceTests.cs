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

public class CreateInstanceTests : BaseDiagnosticVerifierTest<CreateInstanceAnalyzer>
{
	[Test]
	public async Task CreateComponentInstance()
	{
		const string test = @"
using UnityEngine;

class Foo : Component { }

class Camera : MonoBehaviour
{
    public void Update() {
        Foo foo = new Foo();
    }
}
";

		var diagnostic = ExpectDiagnostic(CreateInstanceAnalyzer.ComponentId)
			.WithLocation(9, 19)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task CreateComponentInstanceArgumentOrParenthesis()
	{
		const string test = @"
using UnityEngine;

class Foo : Component { }

class Camera : MonoBehaviour
{
    public void Method(Foo foo) {
        Method(((new Foo())));
    }
}
";

		var diagnostic = ExpectDiagnostic(CreateInstanceAnalyzer.ComponentId)
			.WithLocation(9, 18)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[Test]
	public async Task CreateComponentInstanceTrivia()
	{
		const string test = @"
using UnityEngine;

class Foo : Component { }

class Camera : MonoBehaviour
{
    public void Update() {
        // comment
        Foo foo = /* inner */ new Foo();
        /* comment */
    }
}
";

		var diagnostic = ExpectDiagnostic(CreateInstanceAnalyzer.ComponentId)
			.WithLocation(10, 31)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task CreateMonoBehaviourInstance()
	{
		const string test = @"
using UnityEngine;

class Foo : MonoBehaviour { }

class Camera : MonoBehaviour
{
    public void Update() {
        Foo foo = new Foo();
    }
}
";

		var diagnostic = ExpectDiagnostic(CreateInstanceAnalyzer.ComponentId)
			.WithLocation(9, 19)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task CreateMonoBehaviourInstanceFromNonComponent()
	{
		const string test = @"
using UnityEngine;

class Foo : MonoBehaviour { }

class Program
{
    public void Main() {
        Foo foo = new Foo();
    }
}
";
		var diagnostic = ExpectDiagnostic(CreateInstanceAnalyzer.ComponentId)
			.WithLocation(9, 19)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task CreateScriptableObjectInstance()
	{
		const string test = @"
using UnityEngine;

class Foo : ScriptableObject { }

class Camera : MonoBehaviour
{
    public void Update() {
        Foo foo = new Foo();
    }
}
";

		var diagnostic = ExpectDiagnostic(CreateInstanceAnalyzer.ScriptableObjectId)
			.WithLocation(9, 19)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}