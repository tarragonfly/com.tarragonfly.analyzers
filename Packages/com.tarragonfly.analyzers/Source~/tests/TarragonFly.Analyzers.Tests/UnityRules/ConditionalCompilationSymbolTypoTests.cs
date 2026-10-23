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

public class ConditionalCompilationSymbolTypoTests : BaseDiagnosticVerifierTest<ConditionalCompilationSymbolTypoAnalyzer>
{
	[Test]
	public async Task UnityPlatformSymbolTypo()
	{
		const string test = @"
class Test
{
    void Method()
    {
#if UNITTY_STANDALONE_OSX
        return;
#endif
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 5)
			.WithArguments("UNITTY_STANDALONE_OSX", "UNITY_STANDALONE_OSX");

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_STANDALONE_OSX");

		await VerifyCSharpDiagnosticAsync(context, test, diagnostic);
	}

	[Test]
	public async Task CompoundConditionTypoTrivia()
	{
		const string test = @"
class Test
{
    void Method()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || !UNITTY_STANDALONE_OSX
        return;
#endif
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 72)
			.WithArguments("UNITTY_STANDALONE_OSX", "UNITY_STANDALONE_OSX");

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_EDITOR", "UNITY_EDITOR_OSX", "UNITY_STANDALONE", "UNITY_STANDALONE_OSX", "UNITY_64");

		await VerifyCSharpDiagnosticAsync(context, test, diagnostic);
	}

	[Test]
	public async Task ElifDirectiveTypo()
	{
		const string test = @"
class Test
{
    void Method()
    {
#if UNITY_EDITOR
        return;
#elif UNITTY_STANDALONE_OSX
        return;
#endif
    }
}
";

		var diagnostic = ExpectDiagnostic()
			.WithLocation(8, 7)
			.WithArguments("UNITTY_STANDALONE_OSX", "UNITY_STANDALONE_OSX");

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_EDITOR", "UNITY_STANDALONE_OSX");

		await VerifyCSharpDiagnosticAsync(context, test, diagnostic);
	}

	[TestCase("EATURE_RELEASE", "FEATURE_RELEASE")]
	[TestCase("FEATURE_RELESE", "FEATURE_RELEASE")]
	[TestCase("FEATURE_RELEAS", "FEATURE_RELEASE")]
	[TestCase("FFEATURE_RELEASE", "FEATURE_RELEASE")]
	[TestCase("FEATTURE_RELEASE", "FEATURE_RELEASE")]
	[TestCase("FEATURE_RELEASEE", "FEATURE_RELEASE")]
	[TestCase("GEATURE_RELEASE", "FEATURE_RELEASE")]
	[TestCase("FEATURE_RELEAZE", "FEATURE_RELEASE")]
	[TestCase("FEATURE_RELEASX", "FEATURE_RELEASE")]
	[TestCase("EFATURE_RELEASE", "FEATURE_RELEASE")]
	[TestCase("FETAURE_RELEASE", "FEATURE_RELEASE")]
	[TestCase("FEATURE_RELEAES", "FEATURE_RELEASE")]
	[TestCase("UNITY_EDTIOR", "UNITY_EDITOR")]
	[TestCase("UNITY_6000_3_OR_NEWRE", "UNITY_6000_3_OR_NEWER")]
	[TestCase("UNITY_6000__3_OR_NEWER", "UNITY_6000_3_OR_NEWER")]
	[TestCase("UNITY_6000_3_OR_NEWE", "UNITY_6000_3_OR_NEWER")]
	[TestCase("UNITY_6000_3_OR_NEWER_", "UNITY_6000_3_OR_NEWER")]
	[TestCase("FEATURE_12_V2_RELEAS", "FEATURE_12_V2_RELEASE")]
	[TestCase("FEATURE_1A2", "FEATURE_1B2")]
	[TestCase("ACB", "ABC")]
	public async Task SingleTypo(string symbol, string definedSymbol)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols(definedSymbol);

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 5)
			.WithArguments(symbol, definedSymbol);

		await VerifyCSharpDiagnosticAsync(context, Source(symbol), diagnostic);
	}

	[TestCase("FEATURE_RELXXSE")]
	[TestCase("FFEATURE_RELEASEE")]
	[TestCase("FEATURE_RELEASXX")]
	[TestCase("FETAURE_RELEAS")]
	public async Task MultipleEdits(string symbol)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("FEATURE_RELEASE");

		await VerifyCSharpDiagnosticAsync(context, Source(symbol));
	}

	[TestCase("FEATURE_1", "FEATURE_2")]
	[TestCase("FEATURE_1", "FEATURE_12")]
	[TestCase("FEATURE_12", "FEATURE_1")]
	[TestCase("FEATURE_12", "FEATURE_21")]
	[TestCase("FEATURE_01", "FEATURE_1")]
	[TestCase("FEATURE", "FEATURE1")]
	[TestCase("FEATURE1", "FEATURE")]
	[TestCase("FEATURE_1_2", "FEATURE_12")]
	[TestCase("FEATURE_12", "FEATURE_1_2")]
	[TestCase("FEATURE_1A2", "FEATURE_12")]
	[TestCase("FEATURE_V2_X3", "FEATURE_V2_X4")]
	[TestCase("FEATURE_V2_X3", "FEATURE_V2_X")]
	[TestCase("FEATURE_V2_X", "FEATURE_V2_X3")]
	public async Task DifferentNumericComponents(string symbol, string definedSymbol)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols(definedSymbol);

		await VerifyCSharpDiagnosticAsync(context, Source(symbol));
	}

	[TestCase("UNITY_5")]
	[TestCase("UNITY_2019_4")]
	[TestCase("UNITY_2019_4_40")]
	[TestCase("UNITY_6000_2")]
	[TestCase("UNITY_6000_3_8")]
	[TestCase("UNITY_6000_4_OR_NEWER")]
	[TestCase("UNITY_6000_5_OR_NEWER")]
	[TestCase("UNITY_9999_9_OR_NEWER")]
	public async Task InactiveUnityVersionSymbol(string symbol)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_6000", "UNITY_6000_3", "UNITY_6000_3_9",
				"UNITY_6000_0_OR_NEWER", "UNITY_6000_1_OR_NEWER", "UNITY_6000_2_OR_NEWER", "UNITY_6000_3_OR_NEWER");

		await VerifyCSharpDiagnosticAsync(context, Source(symbol));
	}

	[TestCase("UNITY_6000")]
	[TestCase("UNITY_6000_4")]
	[TestCase("UNITY_6000_4_1")]
	[TestCase("UNITY_6000_4_OR_NEWER")]
	public async Task UnityVersionSymbolResemblingCustomSymbol(string symbol)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols(symbol + "_");

		await VerifyCSharpDiagnosticAsync(context, Source(symbol));
	}

	[TestCase("UNITY_PS4 || UNITY_PS5")]
	[TestCase("UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX")]
	[TestCase("UNITY_EDITOR || UNITY_STANDALONE || UNITY_PS4 || UNITY_PS5 || UNITY_WSA || UNITY_ANDROID || UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS")]
	public async Task InactivePlatformSymbols(string condition)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_EDITOR", "UNITY_EDITOR_WIN", "UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "UNITY_64");

		await VerifyCSharpDiagnosticAsync(context, Source(condition));
	}

	[Test]
	public async Task InactiveVersionSymbolsInElif()
	{
		const string test = @"
class Test
{
    void Method()
    {
#if UNITY_PS4 || UNITY_PS5
        return;
#elif !UNITY_6000_4_OR_NEWER && !UNITY_6000_5_OR_NEWER
        return;
#endif
    }
}
";

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "UNITY_64", "UNITY_6000_3_OR_NEWER");

		await VerifyCSharpDiagnosticAsync(context, test);
	}

	[TestCase("FEATURE_RELEASX", "FEATURE_RELEASE", "FEATURE_RELEASY")]
	[TestCase("FEATURE_RELEAS", "FEATURE_RELEASE", "FEATURE_RELEAZ")]
	[TestCase("UNITY_EDTIOR", "UNITY_EDITOR", "UNITY_EDTIOS")]
	public async Task AmbiguousSymbol(string symbol, string firstCandidate, string secondCandidate)
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols(firstCandidate, secondCandidate);

		await VerifyCSharpDiagnosticAsync(context, Source(symbol));

		context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols(secondCandidate, firstCandidate);

		await VerifyCSharpDiagnosticAsync(context, Source(symbol));
	}

	[Test]
	public async Task DuplicateDefinedSymbolsAreNotAmbiguous()
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("FEATURE_RELEASE", "FEATURE_RELEASE");

		var diagnostic = ExpectDiagnostic()
			.WithLocation(6, 5)
			.WithArguments("FEATURE_RELEAS", "FEATURE_RELEASE");

		await VerifyCSharpDiagnosticAsync(context, Source("FEATURE_RELEAS"), diagnostic);
	}

	[Test]
	public async Task TypoForInactivePlatform()
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_EDITOR", "UNITY_EDITOR_WIN", "UNITY_STANDALONE", "UNITY_STANDALONE_WIN", "UNITY_64");

		await VerifyCSharpDiagnosticAsync(context, Source("UNITTY_STANDALONE_OSX"));
	}

	[Test]
	public async Task ShortSymbol()
	{
		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("ABC");

		await VerifyCSharpDiagnosticAsync(context, Source("AB"));
	}

	[Test]
	public async Task DefinedProjectSymbol()
	{
		const string test = @"
class Test
{
    void Method()
    {
#if FEATURE_RELEASE
        return;
#endif
    }
}
";

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("FEATURE_RELEASE");

		await VerifyCSharpDiagnosticAsync(context, test);
	}

	[Test]
	public async Task DistantUndefinedSymbol()
	{
		const string test = @"
class Test
{
    void Method()
    {
#if OTHER_SYMBOL
        return;
#endif
    }
}
";

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("FEATURE_RELEASE");

		await VerifyCSharpDiagnosticAsync(context, test);
	}

	[Test]
	public async Task LocallyDefinedSymbol()
	{
		const string test = @"
#define UNITTY_STANDALONE_OSX

class Test
{
    void Method()
    {
#if UNITTY_STANDALONE_OSX
        return;
#endif
    }
}
";

		var context = AnalyzerVerificationContext.Default
			.WithPreprocessorSymbols("UNITY_STANDALONE_OSX");

		await VerifyCSharpDiagnosticAsync(context, test);
	}

	private static string Source(string condition)
	{
		return $@"
class Test
{{
    void Method()
    {{
#if {condition}
        return;
#endif
    }}
}}
";
	}
}