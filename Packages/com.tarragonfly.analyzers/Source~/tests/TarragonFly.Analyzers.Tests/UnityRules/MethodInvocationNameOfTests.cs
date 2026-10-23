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

public class MethodInvocationNameOfTests : BaseDiagnosticVerifierTest<MethodInvocationAnalyzer>
{
	[Test]
	public async Task TestInvoke()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Start()
    {
        Invoke(""InvokeMe"", 10.0f);
    }

    private void InvokeMe()
    {
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestInvokeTrivia()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Start()
    {
        // comment
        Invoke(/* inner */ ""InvokeMe"" /* outer */, 10.0f);
        /* comment */
    }

    private void InvokeMe()
    {
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestInvokeRepeating()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Start()
    {
        InvokeRepeating(""InvokeMe"", 10.0f, 10.0f);
    }

    private void InvokeMe()
    {
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestCancelInvokeNoArgument()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        CancelInvoke();
    }
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TestCancelInvoke()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Update()
    {
        CancelInvoke(""InvokeMe"");
    }

    private void InvokeMe()
    {
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}


	[TestCase("IsInvoking(\"InvokeMe\")", "IsInvoking(nameof(InvokeMe))")]
	[TestCase("this.IsInvoking(\"InvokeMe\")", "this.IsInvoking(nameof(InvokeMe))")]
	[TestCase("IsInvoking(methodName: \"InvokeMe\")", "IsInvoking(nameof(InvokeMe))")]
	[TestCase("IsInvoking(@\"InvokeMe\")", "IsInvoking(nameof(InvokeMe))")]
	[TestCase("IsInvoking(/* inner */ \"InvokeMe\" /* outer */)", "IsInvoking(/* inner */ nameof(InvokeMe) /* outer */)")]
	public async Task TestIsInvoking(string expression, string fixedExpression)
	{
		var test = $@"
using UnityEngine;

class Camera : MonoBehaviour
{{
    bool HasPendingInvoke()
    {{
        return {expression};
    }}

    private void InvokeMe()
    {{
    }}
}}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 16)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestIsInvokingNoArgument()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    bool HasPendingInvoke()
    {
        return IsInvoking();
    }
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TestInvokeRepeatingThis()
	{
		const string test = @"
using UnityEngine;

class Camera : MonoBehaviour
{
    void Start()
    {
        this.InvokeRepeating(""InvokeMe"", 10.0f, 10.0f);
    }

    private void InvokeMe()
    {
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestInvokeRepeatingMixedTypes()
	{
		const string test = @"
using UnityEngine;

class A : MonoBehaviour
{
    private B b = null;

    void Update()
    {
        b.InvokeRepeating(""Foo"", 1.0f, 1.0f);
	}

    class B : MonoBehaviour
    {
        void Foo()
        {
        }
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(10, 9)
			.WithArguments("Foo");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestInvokeNoMonoBehaviour()
	{
		const string test = @"
class Foo
{
    void Bar()
    {
        this.Invoke(""InvokeMe"", 10.0f);
    }

    private void Invoke(string name, object param) 
    {
    }

    private void InvokeMe()
    {
    }
}";

		await VerifyCSharpDiagnosticAsync(test);
	}

	[Test]
	public async Task TestStartCoroutine()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Start()
    {
        StartCoroutine(""InvokeMe"");
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestStartCoroutineArgumentOrParenthesis()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Method(Coroutine c)
    {
        Method(((StartCoroutine(""InvokeMe""))));
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 18)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TestStopCoroutine()
	{
		const string test = @"
using UnityEngine;
using System.Collections;

class Camera : MonoBehaviour
{
    void Start()
    {
        StopCoroutine(""InvokeMe"");
    }

    private IEnumerator InvokeMe()
    {
		return null;
    }
}";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 9)
			.WithArguments("InvokeMe");

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}