/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: NUnit, TF rule IDs, no code fix checks.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using TarragonFly.Analyzers.Rules.Unity;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public class TextMeshProSetTextTests : BaseDiagnosticVerifierTest<TextMeshProSetTextAnalyzer>
{
	private readonly AnalyzerVerificationContext _context = AnalyzerVerificationContext.Default
		.WithAnalyzerFilter("CS8019"); // Shared test sources can contain unnecessary using directives.

	[TestCase("TMP_Text", "label.text = builder.ToString();")]
	[TestCase("TextMeshPro", "label.text = builder.ToString();")]
	[TestCase("TextMeshProUGUI", "label.text = builder.ToString();")]
	[TestCase("TMP_Text", "label.SetText(builder.ToString());")]
	[TestCase("TextMeshPro", "label.SetText(builder.ToString());")]
	[TestCase("TextMeshProUGUI", "label.SetText(builder.ToString());")]
	[TestCase("TMP_Text", "label.SetText(sourceText: builder.ToString());")]
	[TestCase("TMP_Text", "label?.SetText(builder.ToString());", 15)]
	[TestCase("TMP_Text", "label.text = (builder.ToString());")]
	[TestCase("TMP_Text", "label.SetText((builder.ToString()));")]
	[TestCase("TMP_Text", "label.text = new StringBuilder().Append(\"Score\").ToString();")]
	public async Task StringBuilder(string textType, string statement, int column = 9)
	{
		await VerifyCSharpDiagnosticAsync(_context,
			CreateSource(statement, "StringBuilder builder", textType),
			ExpectDiagnostic().WithLocation(9, column).WithArguments("SetText(StringBuilder)"));
	}

	[TestCase("label.text = new string(buffer);", "SetText(char[])")]
	[TestCase("label.SetText(new string(buffer));", "SetText(char[])")]
	[TestCase("label.text = new(buffer);", "SetText(char[])")]
	[TestCase("label.text = new string(buffer, start, count);", "SetText(char[], int, int)")]
	[TestCase("label.SetText(new string(buffer, start, count));", "SetText(char[], int, int)")]
	[TestCase("label.text = new string(value: buffer, startIndex: start, length: count);", "SetText(char[], int, int)")]
	[TestCase("label.text = new string(length: count, value: buffer, startIndex: start);", "SetText(char[], int, int)")]
	public async Task CharacterArray(string statement, string overload)
	{
		await VerifyCSharpDiagnosticAsync(_context,
			CreateSource(statement, "char[] buffer, int start, int count"),
			ExpectDiagnostic().WithLocation(9, 9).WithArguments(overload));
	}

	[TestCase("byte")]
	[TestCase("sbyte")]
	[TestCase("short")]
	[TestCase("ushort")]
	[TestCase("int")]
	[TestCase("uint")]
	[TestCase("float")]
	public async Task NumericInterpolation(string type)
	{
		await VerifyCSharpDiagnosticAsync(_context,
			CreateSource("label.text = $\"Score: {value}\";", $"{type} value"),
			ExpectDiagnostic().WithLocation(9, 9).WithArguments("SetText(string, float)"));
	}

	[TestCase("label.SetText($\"Score: {value}\");")]
	[TestCase("label.SetText(sourceText: $\"Score: {value}\");")]
	[TestCase("label.text = value.ToString();")]
	[TestCase("label.SetText(value.ToString());")]
	[TestCase("label.text = $@\"Score: {value}\";")]
	public async Task NumericText(string statement)
	{
		await VerifyCSharpDiagnosticAsync(_context,
			CreateSource(statement, "float value"),
			ExpectDiagnostic().WithLocation(9, 9).WithArguments("SetText(string, float)"));
	}

	[TestCase(2)]
	[TestCase(3)]
	[TestCase(4)]
	[TestCase(5)]
	[TestCase(6)]
	[TestCase(7)]
	[TestCase(8)]
	public async Task MultipleNumericArguments(int count)
	{
		var content = string.Join(" ", Enumerable.Repeat("{value}", count));
		await VerifyCSharpDiagnosticAsync(_context,
			CreateSource($"label.text = $\"{content}\";", "float value"),
			ExpectDiagnostic().WithLocation(9, 9).WithArguments("SetText(string, float, ...)"));
	}

	[TestCase("label.text = \"Score\";")]
	[TestCase("label.text = text;")]
	[TestCase("label.text = null;")]
	[TestCase("label.text = $\"Score\";")]
	[TestCase("label.text = $\"Name: {text}\";")]
	[TestCase("label.text = $\"{text}: {value}\";")]
	[TestCase("label.SetText(text);")]
	[TestCase("label.SetText((string)null);")]
	[TestCase("label.SetText(\"Score: {0}\", value);")]
	[TestCase("label.SetText(builder);")]
	[TestCase("label.SetText(buffer);")]
	[TestCase("label.SetText(buffer, 0, buffer.Length);")]
	public async Task NoTemporaryStringOrNoSuitableOverload(string statement)
	{
		await VerifyCSharpDiagnosticAsync(_context, CreateSource(statement, "string text, float value, StringBuilder builder, char[] buffer"));
	}

	[TestCase("label.text = builder.ToString(0, 1);")]
	[TestCase("label.text = builder?.ToString();")]
	[TestCase("label.text += builder.ToString();")]
	[TestCase("label.SetText(builder.ToString(), 1f);")]
	[TestCase("label.SetText(arg0: 1f, sourceText: builder.ToString());")]
	[TestCase("label.text = new string('a', 10);")]
	[TestCase("label.text = new string((char[])null);")]
	[TestCase("label.text = new string(value: (char[])null);")]
	[TestCase("var copy = new TextMeshProUGUI { text = builder.ToString() };")]
	public async Task UnsupportedBufferUsage(string statement)
	{
		await VerifyCSharpDiagnosticAsync(_context, CreateSource(statement, "StringBuilder builder"));
	}

	[TestCase("double")]
	[TestCase("decimal")]
	[TestCase("long")]
	[TestCase("ulong")]
	[TestCase("float?")]
	[TestCase("bool")]
	[TestCase("char")]
	[TestCase("object")]
	public async Task UnsupportedNumericType(string type)
	{
		await VerifyCSharpDiagnosticAsync(_context, CreateSource("label.text = $\"Value: {value}\";", $"{type} value"));
		await VerifyCSharpDiagnosticAsync(_context, CreateSource("label.text = value.ToString();", $"{type} value"));
	}

	[TestCase("label.text = $\"{value:F2}\";")]
	[TestCase("label.text = $\"{value:0.00}\";")]
	[TestCase("label.text = $\"{value,10}\";")]
	[TestCase("label.text = $\"{{Score}}: {value}\";")]
	[TestCase("label.text = value.ToString(\"F2\");")]
	[TestCase("label.text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);")]
	[TestCase("label.text = string.Format(\"{0:N2}\", value);")]
	[TestCase("label.text = $\"{value} {value} {value} {value} {value} {value} {value} {value} {value}\";")]
	[TestCase("label.SetText($\"{value}\", 1f);")]
	public async Task UnsupportedFormatting(string statement)
	{
		await VerifyCSharpDiagnosticAsync(_context, CreateSource(statement, "float value"));
	}

	[TestCase("16777217")]
	[TestCase("16777217u")]
	[TestCase("12345678")]
	[TestCase("12345678u")]
	[TestCase("12345678f")]
	[TestCase("int.MaxValue")]
	[TestCase("uint.MaxValue")]
	[TestCase("1e20f")]
	[TestCase("float.MaxValue")]
	[TestCase("float.MinValue")]
	[TestCase("float.NaN")]
	[TestCase("float.PositiveInfinity")]
	[TestCase("float.NegativeInfinity")]
	public async Task KnownNumericRangeOrPrecisionLoss(string value)
	{
		await VerifyCSharpDiagnosticAsync(_context, CreateSource($"label.text = $\"Value: {{{value}}}\";", "float unused"));
	}

	[Test]
	public async Task DerivedTextType()
	{
		var source = CreateSource("label.text = builder.ToString();", "StringBuilder builder", "CustomText")
			+ @"
class CustomText : TextMeshProUGUI
{
}
";
		await VerifyCSharpDiagnosticAsync(_context, source, ExpectDiagnostic().WithLocation(9, 9).WithArguments("SetText(StringBuilder)"));
	}

	[TestCase("label.text = builder.ToString();", "public override string text { get; set; }")]
	[TestCase("label.text = builder.ToString();", "public new string text { get; set; }")]
	[TestCase("label.SetText(builder.ToString());", "public new void SetText(string sourceText, bool syncTextInputBox = true) { }")]
	public async Task CustomTextMembers(string statement, string member)
	{
		var source = CreateSource(statement, "StringBuilder builder", "CustomText")
			+ $@"
class CustomText : TextMeshProUGUI
{{
    {member}
}}
";
		await VerifyCSharpDiagnosticAsync(_context, source);
	}

	[TestCase("label.text = builder.ToString();")]
	[TestCase("label.SetText(builder.ToString());")]
	public async Task UnrelatedType(string statement)
	{
		var source = CreateSource(statement, "StringBuilder builder", "OtherText")
			+ @"
class OtherText
{
    public string text { get; set; }
    public void SetText(string value) { }
}
";
		await VerifyCSharpDiagnosticAsync(_context, source);
	}

	[Test]
	public async Task AssignmentValueIsUsed()
	{
		const string source = @"
using System.Text;
using TMPro;

class Example
{
    string UpdateText(TMP_Text label, StringBuilder builder)
    {
        return label.text = builder.ToString();
    }
}
";
		await VerifyCSharpDiagnosticAsync(_context, source);
	}

	[Test]
	public async Task ImplicitReceiver()
	{
		const string source = @"
using System.Text;
using TMPro;

class Example : TextMeshProUGUI
{
    void UpdateText(StringBuilder builder)
    {
        text = builder.ToString();
        SetText(builder.ToString());
    }
}
";
		await VerifyCSharpDiagnosticAsync(_context, source,
			ExpectDiagnostic().WithLocation(9, 9).WithArguments("SetText(StringBuilder)"),
			ExpectDiagnostic().WithLocation(10, 9).WithArguments("SetText(StringBuilder)"));
	}

	[Test]
	public async Task Trivia()
	{
		await VerifyCSharpDiagnosticAsync(_context,
			CreateSource("label.text = /* source */ builder.ToString(); // display", "StringBuilder builder"),
			ExpectDiagnostic().WithLocation(9, 9).WithArguments("SetText(StringBuilder)"));
	}

	[Test]
	public async Task TextMeshProNotReferenced()
	{
		await VerifyCSharpDiagnosticWithoutReferenceAsync("Unity.TextMeshPro.dll", "class Example { }");
	}

	private static string CreateSource(string statement, string parameters, string textType = "TMP_Text")
	{
		return $@"
using System.Text;
using TMPro;

class Example
{{
    void UpdateText({textType} label, {parameters})
    {{
        {statement}
    }}
}}
";
	}
}