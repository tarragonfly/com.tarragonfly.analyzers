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

public class ImproperMenuItemMethodTests : BaseDiagnosticVerifierTest<ImproperMenuItemMethodAnalyzer>
{
	[Test]
	public async Task MissingStaticDeclaration()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class Camera : MonoBehaviour
{
    [MenuItem(""Name"")]
    private void Menu1()
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(7, 5);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task MissingStaticDeclarationTrivia()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class Camera : MonoBehaviour
{
    // comment
    [MenuItem(""Name"")]
    private void Menu1() /* comment */
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 5);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task PreserveMenuCommand()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class Camera : MonoBehaviour
{
    [MenuItem(""Name"")]
    private void Menu1(MenuCommand command)
    {
    }
}
";

		var diagnostic = ExpectDiagnostic().WithLocation(7, 5);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}