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

public class TagComparisonTests : BaseDiagnosticVerifierTest<TagComparisonAnalyzer>
{
	[Test]
	public async Task TagAsIdentifier()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(tag == ""tag1"");
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierTrivia()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        // comment
        Debug.Log(/* inner */ tag == ""tag1"" /* outer */);
        /* comment */
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(9, 31);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierInvoke()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(tag.Equals(""tag1""));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierNeqInvoke()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(!tag.Equals(""tag1""));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 20);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierThisPrefix()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(this.tag == ""tag1"");
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierNeqNullRhs()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(null != tag);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagProperty()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(transform.tag == ""tag2"");
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagPropertyForGameObject()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(gameObject.tag == ""tag2"");
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierRhs()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(""tag3"" == tag);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagAsIdentifierRhsInvoke()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(""tag3"".Equals(tag));
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagPropertyRhs()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(""tag4"" == transform.tag);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task TagPropertyNeqNullRhs()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        Debug.Log(null != transform.tag);
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 19);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ObjectEqualsExplicit()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        if (object.Equals(transform.tag, null))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 13);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}

	[Test]
	public async Task ObjectEqualsImplicit()
	{
		const string test = @"
using UnityEngine;

public class Camera : MonoBehaviour
{
    private void Update()
    {
        if (Equals(null, transform.tag))
            return;
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 13);

		await VerifyCSharpDiagnosticAsync(test, diagnostic);
	}
}