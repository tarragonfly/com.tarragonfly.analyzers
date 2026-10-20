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

public class NonAllocatingArrayAccessTests : BaseDiagnosticVerifierTest<NonAllocatingArrayAccessAnalyzer>
{
	[TestCase("Collision", "value.contacts.Length", "value.contactCount", "contacts", "contactCount")]
	[TestCase("Collision2D", "value.contacts.Length", "value.contactCount", "contacts", "contactCount")]
	[TestCase("object", "Input.touches.Length", "Input.touchCount", "touches", "touchCount")]
	[TestCase("object", "Input.accelerationEvents.Length", "Input.accelerationEventCount", "accelerationEvents", "accelerationEventCount")]
	[TestCase("Collision", "value.contacts[i]", "value.GetContact(i)", "contacts", "GetContact")]
	[TestCase("Collision2D", "value.contacts[i]", "value.GetContact(i)", "contacts", "GetContact")]
	[TestCase("object", "Input.touches[i]", "Input.GetTouch(i)", "touches", "GetTouch")]
	[TestCase("object", "Input.accelerationEvents[i]", "Input.GetAccelerationEvent(i)", "accelerationEvents", "GetAccelerationEvent")]
	[TestCase("Collision", "value.contacts[0].normal", "value.GetContact(0).normal", "contacts", "GetContact")]
	[TestCase("Collision2D", "value.contacts[0].normal", "value.GetContact(0).normal", "contacts", "GetContact")]
	[TestCase("object", "Input.touches[0].position", "Input.GetTouch(0).position", "touches", "GetTouch")]
	[TestCase("object", "Input.accelerationEvents[0].acceleration", "Input.GetAccelerationEvent(0).acceleration", "accelerationEvents", "GetAccelerationEvent")]
	[TestCase("Collision", "value.contacts[i++]", "value.GetContact(i++)", "contacts", "GetContact")]
	[TestCase("Collision2D", "value.contacts[i++]", "value.GetContact(i++)", "contacts", "GetContact")]
	[TestCase("object", "Input.touches[i++]", "Input.GetTouch(i++)", "touches", "GetTouch")]
	[TestCase("object", "Input.accelerationEvents[i++]", "Input.GetAccelerationEvent(i++)", "accelerationEvents", "GetAccelerationEvent")]
	public async Task NonAllocatingRead(string type, string expression, string fixedExpression, string property, string replacement)
	{
		var test = Source(type, $"Debug.Log({expression});");
		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19)
			.WithArguments(replacement, property);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[TestCase("InputAlias.touches.Length", "InputAlias.touchCount", "touchCount")]
	[TestCase("InputAlias.touches[i]", "InputAlias.GetTouch(i)", "GetTouch")]
	public async Task AliasedType(string expression, string fixedExpression, string replacement)
	{
		const string alias = "using InputAlias = UnityEngine.Input;";
		var test = Source("object", $"Debug.Log({expression});", alias);
		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19)
			.WithArguments(replacement, "touches");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[TestCase("value./* property */contacts[i /* index */]", "value./* property */GetContact(i /* index */)", "GetContact")]
	[TestCase("value.contacts /* array */.Length", "value.contactCount /* array */", "contactCount")]
	public async Task PreserveTrivia(string expression, string fixedExpression, string replacement)
	{
		var test = Source("Collision", $"Debug.Log({expression});");
		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19)
			.WithArguments(replacement, "contacts");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[TestCase("Collision", "Debug.Log(value.contactCount);")]
	[TestCase("Collision", "Debug.Log(value.GetContact(i));")]
	[TestCase("Collision2D", "Debug.Log(value.contactCount);")]
	[TestCase("Collision2D", "Debug.Log(value.GetContact(i));")]
	[TestCase("object", "Debug.Log(Input.touchCount);")]
	[TestCase("object", "Debug.Log(Input.GetTouch(i));")]
	[TestCase("object", "Debug.Log(Input.accelerationEventCount);")]
	[TestCase("object", "Debug.Log(Input.GetAccelerationEvent(i));")]
	[TestCase("Collision", "Debug.Log(value.contacts);")]
	[TestCase("object", "Debug.Log(Input.touches);")]
	[TestCase("object", "Debug.Log(Input.accelerationEvents);")]
	[TestCase("Collision", "foreach (var contact in value.contacts) Debug.Log(contact);")]
	[TestCase("object", "foreach (var touch in Input.touches) Debug.Log(touch);")]
	[TestCase("object", "foreach (var accelerationEvent in Input.accelerationEvents) Debug.Log(accelerationEvent);")]
	[TestCase("Collision", "Debug.Log(value.contacts.LongLength);")]
	[TestCase("Collision", "Debug.Log(nameof(value.contacts.Length));")]
	[TestCase("object", "Debug.Log(nameof(Input.touches.Length));")]
	[TestCase("ContactPoint[]", "Debug.Log(value.Length);")]
	[TestCase("Touch[]", "Debug.Log(value[i]);")]
	public async Task OtherUsage(string type, string statement)
	{
		await VerifyCSharpDiagnosticAsync(Source(type, statement));
	}

	[TestCase("Collision", "value.contacts[i] = default;")]
	[TestCase("Collision", "(value.contacts[i], i) = (default, 0);")]
	[TestCase("Collision", "ref var contact = ref value.contacts[i]; Debug.Log(contact);")]
	[TestCase("Collision", "void Modify(ref ContactPoint p) { } Modify(ref value.contacts[i]);")]
	[TestCase("Collision2D", "void Modify(out ContactPoint2D p) { p = default; } Modify(out value.contacts[i]);")]
	[TestCase("object", "void Inspect(in Touch touch) { } Inspect(in Input.touches[i]);")]
	[TestCase("Collision", "void Inspect(in ContactPoint p) { } Inspect(value.contacts[i]);")]
	[TestCase("Collision2D", "void Inspect(in ContactPoint2D p) { } Inspect(value.contacts[i]);")]
	[TestCase("object", "void Inspect(in Touch touch) { } Inspect((Input.touches[i]));")]
	[TestCase("Collision", "Debug.Log(value.contacts[i].ToString());")]
	[TestCase("object", "Input.touches[i].position = Vector2.zero;")]
	[TestCase("object", "Input.accelerationEvents[i] = default;")]
	[TestCase("object", "void Modify(ref AccelerationEvent accelerationEvent) { } Modify(ref Input.accelerationEvents[i]);")]
	[TestCase("object", "Input.touches[i]!.position = Vector2.zero;")]
	[TestCase("Collision", "ref var contact = ref value.contacts[i]!; Debug.Log(contact);")]
	[TestCase("object", "Input.touches[i].tapCount++;")]
	[TestCase("object", "Input.touches[i].tapCount += 1;")]
	[TestCase("Collision", "Debug.Log(value.contacts[(long)i]);")]
	[TestCase("Collision2D", "Debug.Log(value.contacts[(uint)i]);")]
	[TestCase("object", "Debug.Log(Input.touches[(long)i]);")]
	[TestCase("object", "Debug.Log(Input.accelerationEvents[(long)i]);")]
	public async Task NotAValueReadWithIntIndex(string type, string statement)
	{
		await VerifyCSharpDiagnosticAsync(Source(type, statement));
	}

	[Test]
	public async Task UnrelatedProperty()
	{
		var test = Source("Other", "Debug.Log(value.contacts.Length); Debug.Log(value.contacts[i]);") + @"
class Other
{
    public int[] contacts => new int[1];
    public int contactCount => 1;
    public int GetContact(int index) => 0;
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[TestCase("value.contacts.Length")]
	[TestCase("value.contacts[i]")]
	public async Task HiddenReplacement(string expression)
	{
		var test = Source("DerivedCollision", $"Debug.Log({expression});") + @"
class DerivedCollision : Collision
{
    public new int contactCount => 99;
    public new ContactPoint GetContact(int index) => default;
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task AwaitedIndex()
	{
		const string test = @"
using UnityEngine;
using System.Threading.Tasks;

class Example
{
    async Task<ContactPoint> Read(Collision collision, Task<int> index)
    {
        return collision.contacts[await index];
    }
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	private static string Source(string type, string statement, string extraUsing = "")
	{
		return $@"
using UnityEngine;
{extraUsing}
class Example : MonoBehaviour
{{
    void Method({type} value, int i)
    {{
        {statement}
    }}
}}";
	}
}