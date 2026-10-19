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

public class PropertyDrawerOnGUITests : BaseDiagnosticVerifierTest<PropertyDrawerOnGUIAnalyzer>
{
	[Test]
	public async Task TestOnGUI()
	{
		const string test = @"
using UnityEditor;

class MyDrawer : PropertyDrawer
{
	public void Foo() {
		OnGUI(default, default, default);
	}
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(7, 3);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestOnGUIOverride()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class DerivedDrawer : PropertyDrawer
{
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
	}
}

class MyDrawer : DerivedDrawer
{
	public void Foo() {
		OnGUI(default, default, default);
	}
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TestBaseOnGUI()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class MyDrawer : PropertyDrawer
{
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		base.OnGUI(position, property, label);
	}
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 3);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestBaseOnGUIOverride()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class DerivedDrawer : PropertyDrawer
{
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
	}
}

class MyDrawer : DerivedDrawer
{
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		base.OnGUI(position, property, label);
	}
}
";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TestBaseOnGUITrivia()
	{
		const string test = @"
using UnityEngine;
using UnityEditor;

class MyDrawer : PropertyDrawer
{
	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
		// comment expected to be removed
		base.OnGUI(position, property, label);
		// trailing comment
	}
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 3);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}