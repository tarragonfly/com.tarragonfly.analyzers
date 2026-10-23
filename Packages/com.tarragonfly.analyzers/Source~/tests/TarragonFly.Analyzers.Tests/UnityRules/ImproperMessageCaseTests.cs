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

public class ImproperMessageCaseTests : BaseDiagnosticVerifierTest<ImproperMessageCaseAnalyzer>
{
	[Test]
	public async Task ProperlyCasedUpdate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void Update()
    {
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ImproperlyCasedUpdate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private void UPDATE()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 18)
			.WithArguments("UPDATE");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ImproperlyCasedAwaitableUpdate()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private async Awaitable UPDATE()
    {
    }
}
";

		var context = AnalyzerVerificationContext
			.Default
			.WithAnalyzerFilter("CS1998");

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 29)
			.WithArguments("UPDATE");

		await VerifyCSharpDiagnosticAsync(context, test, diagnostic);
	}

	[Test]
	public async Task ImproperlyCasedStaticUpdateIgnored()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    private static void UPDATE()
    {
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ImproperlyCasedRealStaticMessage()
	{
		const string test = @"
using UnityEditor;

class App : AssetPostprocessor
{
    static bool OnPREGeneratingCSProjectFiles()
    {
        return false;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 17)
			.WithArguments("OnPREGeneratingCSProjectFiles");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ImproperlyCasedAwaitableRealStaticMessage()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class App : AssetPostprocessor
{
    async static Awaitable<bool> OnPREGeneratingCSProjectFiles()
    {
        return false;
    }
}
";

		var context = AnalyzerVerificationContext
			.Default
			.WithAnalyzerFilter("CS1998");

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 34)
			.WithArguments("OnPREGeneratingCSProjectFiles");

		await VerifyCSharpDiagnosticAsync(context, test, diagnostic);
	}


	[Test]
	public async Task ProperlyCasedOnPostprocessAllAssets()
	{
		const string test = @"
using UnityEditor;

class App : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ProperlyCasedOnPostprocessAllAssetsOverload()
	{
		const string test = @"
using UnityEditor;

class App : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
    {
    }
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ImproperlyCasedPostprocessAllAssets()
	{
		const string test = @"
using UnityEditor;

class App : AssetPostprocessor
{
    static void OnPostPROCESSAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 17)
			.WithArguments("OnPostPROCESSAllAssets");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ImproperlyCasedOnPostprocessAllAssetsOverload()
	{
		const string test = @"
using UnityEditor;

class App : AssetPostprocessor
{
    static void OnPostProcessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
    {
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 17)
			.WithArguments("OnPostProcessAllAssets");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ImproperlyCasedUpdateTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    // comment
    private void /* inner */ UPDATE /* outer */ ()
    {
    }
    /* comment */
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 30)
			.WithArguments("UPDATE");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}