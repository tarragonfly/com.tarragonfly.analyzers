/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: NUnit, no code fix or suppressor bases.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;

namespace TarragonFly.Analyzers.Tests.UnityRules;

public abstract class BaseDiagnosticVerifierTest<TAnalyzer> : DiagnosticVerifier
	where TAnalyzer : DiagnosticAnalyzer, new()
{
	internal const string InterfaceTest = @"
using UnityEngine;

interface IFailure
{
	void FixedUpdate();
}

class Failure : MonoBehaviour, IFailure {

    void IFailure.FixedUpdate() {
    }
}
";

	[Test]
	public async Task DoNotFailWithInterfaceMembers()
	{
		await VerifyCSharpDiagnosticAsync(InterfaceTest);
	}

	protected override TAnalyzer GetCSharpDiagnosticAnalyzer()
	{
		return new TAnalyzer();
	}

	// Some upstream tests only apply to Unity versions that have a given API.
	protected static void SkipUnless(bool condition, string reason)
	{
		if (!condition)
			Assert.Ignore(reason);
	}

	protected bool MethodExists(string assemblyName, string typeName, string methodName)
	{
		var unityAssemblies = UnityAssemblies().ToArray();
		var assemblyFilePath = unityAssemblies.First(f => Path.GetFileNameWithoutExtension(f) == assemblyName);

		var resolver = new PathAssemblyResolver(unityAssemblies);
		using var ctx = new MetadataLoadContext(resolver);

		var assembly = ctx.LoadFromAssemblyPath(assemblyFilePath);
		Assert.That(assembly, Is.Not.Null);

		var type = assembly.GetType(typeName, throwOnError: false);
		if (type == null)
			return false;

		var method = type.GetMethod(methodName);
		return method != null;
	}
}