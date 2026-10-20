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

public class LoadAttributeMethodTests : BaseDiagnosticVerifierTest<LoadAttributeMethodAnalyzer>
{
	[Test]
	public async Task InitializeOnLoadMethodTest()
	{
		const string test = @"
using UnityEditor;

class Loader
{
    [InitializeOnLoadMethod]
    private static void OnLoad() {}
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task InitializeOnLoadMethodFixModifiersTest()
	{
		const string test = @"
using UnityEditor;

class Loader
{
    [InitializeOnLoadMethod]
    private void OnLoad()
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task InitializeOnLoadMethodFixModifiersTrivia()
	{
		const string test = @"
using UnityEditor;

class Loader
{
    // comment
    [InitializeOnLoadMethod]
    private void OnLoad() /* comment */
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task InitializeOnLoadMethodFixParametersTest()
	{
		const string test = @"
using UnityEditor;

class Loader
{
    [InitializeOnLoadMethod]
    private static void OnLoad(int foo, string bar)
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 25)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task InitializeOnLoadMethodFixAllTest()
	{
		const string test = @"
using UnityEditor;

class Loader
{
    [InitializeOnLoadMethod]
    private void OnLoad(int foo, string bar)
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RuntimeInitializeOnLoadMethodTest()
	{
		const string test = @"
using UnityEngine;

class Loader
{
    [RuntimeInitializeOnLoadMethod]
    private static void OnLoad() {
    }
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task RuntimeInitializeOnLoadMethodFixModifiersTest()
	{
		const string test = @"
using UnityEngine;

class Loader
{
    [RuntimeInitializeOnLoadMethod]
    private void OnLoad()
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RuntimeInitializeOnLoadMethodFixParametersTest()
	{
		const string test = @"
using UnityEngine;

class Loader
{
    [RuntimeInitializeOnLoadMethod]
    private static void OnLoad(int foo, string bar)
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 25)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task RuntimeInitializeOnLoadMethodFixAllTest()
	{
		const string test = @"
using UnityEngine;

class Loader
{
    [RuntimeInitializeOnLoadMethod]
    private void OnLoad(int foo, string bar)
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DidReloadScriptsMethodFixModifiersTest()
	{
		const string test = @"
using UnityEditor.Callbacks;

class Loader
{
    [DidReloadScripts]
    private void OnLoad()
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DidReloadScriptsMethodFixParametersTest()
	{
		const string test = @"
using UnityEditor.Callbacks;

class Loader
{
    [DidReloadScripts]
    private static void OnLoad(int foo, string bar)
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 25)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task DidReloadScriptsMethodFixAllTest()
	{
		const string test = @"
using UnityEditor.Callbacks;

class Loader
{
    [DidReloadScripts]
    private void OnLoad(int foo, string bar)
    {
        // keep
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 18)
			.WithArguments("OnLoad");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}