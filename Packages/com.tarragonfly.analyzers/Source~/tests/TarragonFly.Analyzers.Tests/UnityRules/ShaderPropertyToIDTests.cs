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

public class ShaderPropertyToIDTests : BaseDiagnosticVerifierTest<ShaderPropertyToIDAnalyzer>
{
	private const string ColorCall = "Shader.PropertyToID(\"_Color\")";
	private const string ColorField = "    private static readonly int ColorId = Shader.PropertyToID(\"_Color\");\n";

	[TestCase("Shader.PropertyToID(\"_Color\")")]
	[TestCase("Shader.PropertyToID(name: \"_Color\")")]
	[TestCase("Shader.PropertyToID(@\"_Color\")")]
	[TestCase("UnityEngine.Shader.PropertyToID(\"_Color\")")]
	[TestCase("global::UnityEngine.Shader.PropertyToID(\"_Color\")")]
	public async Task LiteralCall(string expression)
	{
		var test = Source(expression, "0");
		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 16)
			.WithArguments("_Color");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[TestCase("Shader.PropertyToID(PropertyName)")]
	[TestCase("Shader.PropertyToID(\"_\" + \"Color\")")]
	[TestCase("Shader.PropertyToID(nameof(PropertyName))")]
	public async Task NonLiteralConstantsAreExcluded(string expression)
	{
		const string member = "    private const string PropertyName = \"_Color\";\n";
		await VerifyCSharpDiagnosticAsync(Source(expression, expression, member));
	}

	[Test]
	public async Task LocalConstantsAreExcluded()
	{
		const string setup = "        const string propertyName = \"_Color\";\n";
		const string calls = "Shader.PropertyToID(propertyName) + Shader.PropertyToID(propertyName)";
		await VerifyCSharpDiagnosticAsync(Source(calls, "0", firstSetup: setup));
	}

	[Test]
	public async Task CallsInOneMethod()
	{
		var test = Source(ColorCall + " + " + ColorCall, "0");
		var first = ExpectDiagnostic().WithLocation(8, 16).WithArguments("_Color");
		var second = ExpectDiagnostic().WithLocation(8, 16 + ColorCall.Length + 3).WithArguments("_Color");

		await VerifyCSharpDiagnosticAsync(test, first, second);
	}

	[TestCase("_Other", "OtherId")]
	[TestCase("_color", "ColorId1")]
	public async Task DifferentPropertyNames(string name, string fieldName)
	{
		var call = $"Shader.PropertyToID(\"{name}\")";
		var test = Source(ColorCall, call);
		var first = ExpectDiagnostic().WithLocation(8, 16).WithArguments("_Color");
		var second = ExpectDiagnostic().WithLocation(13, 16).WithArguments(name);

		await VerifyCSharpDiagnosticAsync(test, first, second);
	}

	[TestCase("Shader.PropertyToID(null)", "Shader.PropertyToID(null)")]
	[TestCase("Shader.PropertyToID(PropertyName)", "Shader.PropertyToID(PropertyName)", "    private string PropertyName = \"_Color\";\n")]
	[TestCase("ColorId", "0", ColorField)]
	[TestCase("ColorId", "OtherColorId", ColorField + "    private static readonly int OtherColorId = Shader.PropertyToID(\"_Color\");\n")]
	public async Task NonLiteralOrCachedCalls(string first, string second, string members = "")
	{
		await VerifyCSharpDiagnosticAsync(Source(first, second, members));
	}

	[Test]
	public async Task SingleCallInUpdate()
	{
		const string test = @"
using UnityEngine;

class Example : MonoBehaviour
{
    void Update()
    {
        Debug.Log(Shader.PropertyToID(""_Color""));
    }
}";

		var diagnostic = ExpectDiagnostic().WithLocation(8, 19).WithArguments("_Color");
		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[TestCase("Shader.PropertyToID(\"_Color\"); Shader.PropertyToID(\"_Color\");")]
	[TestCase("System.Action action = () => Shader.PropertyToID(\"_Color\"); action();")]
	[TestCase("for (Shader.PropertyToID(\"_Color\"); System.Environment.TickCount < 0; Shader.PropertyToID(\"_Color\")) { }")]
	public async Task DiscardedValues(string statement)
	{
		var test = Source("0", "0", firstSetup: "        " + statement + "\n");
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task ExpressionBodiedVoidMethod()
	{
		var test = Source("0", "0", "    void Method() => Shader.PropertyToID(\"_Color\");\n");
		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task UnrelatedMethod()
	{
		const string otherType = @"
static class OtherShader
{
    public static int PropertyToID(string name) => 0;
}";
		var test = Source("OtherShader.PropertyToID(\"_Color\")", "OtherShader.PropertyToID(\"_Color\")") + otherType;

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task SingleCallInNestedType()
	{
		const string test = @"
using UnityEngine;

class Example
{
    class Nested
    {
        int Read() => Shader.PropertyToID(""_Color"");
    }
}";
		var diagnostic = ExpectDiagnostic().WithLocation(8, 23).WithArguments("_Color");
		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task PropertyInitializersAreExcluded()
	{
		const string properties =
			"    int ColorId { get; } = Shader.PropertyToID(\"_Color\");\n" +
			"    int OtherColorId { get; } = Shader.PropertyToID(\"_Color\");\n";
		await VerifyCSharpDiagnosticAsync(Source("ColorId", "OtherColorId", properties));
	}

	private static string Source(string first, string second, string members = "", string declaration = "class Example : MonoBehaviour",
		string extraUsing = "", string methodModifier = "", string firstSetup = "")
	{
		if (members.Length > 0)
			members += "\n";

		return $@"
using UnityEngine;
{extraUsing}
{declaration}
{{
{members}    {methodModifier}int First()
    {{
{firstSetup}        return {first};
    }}

    {methodModifier}int Second()
    {{
        return {second};
    }}
}}
";
	}
}