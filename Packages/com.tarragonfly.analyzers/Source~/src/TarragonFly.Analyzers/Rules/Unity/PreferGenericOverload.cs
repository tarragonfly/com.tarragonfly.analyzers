/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes, any
 *  receiver, AddComponent and CreateInstance, and type names passed as strings.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using TarragonFly.Analyzers.Rules.Unity.Resources;

namespace TarragonFly.Analyzers.Rules.Unity;

// UNT0003 only reported GetComponent(typeof(T)) called without a receiver. TF4003 also reports
// other.GetComponent(typeof(T)), AddComponent(typeof(T)), ScriptableObject.CreateInstance(typeof(T)),
// and GetComponent("Name") or CreateInstance("Name") with a constant string. A string is only looked
// up when the game runs, so the message names the type it matches, or says no component or
// ScriptableObject type has that name, since the call then returns null. Unity's own string
// AddComponent is a compile error already. Arguments that aren't typeof or a constant are
// skipped, as they usually come from data the generic form can't take.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class PreferGenericOverloadAnalyzer : BaseGetComponentAnalyzer
{
	private const string RuleId = "TF4003";

	internal static readonly DiagnosticDescriptor Rule = Descriptors.Create(
		id: RuleId,
		title: Strings.PreferGenericOverloadDiagnosticTitle,
		messageFormat: Strings.PreferGenericOverloadDiagnosticMessageFormat,
		area: DiagnosticCategory.TypeSafety,
		severity: DiagnosticSeverity.Warning,
		enabledByDefault: true,
		checks: CodeKind.All,
		sourceRule: "UNT0003",
		description: Strings.PreferGenericOverloadDiagnosticDescription);

	private const string AddComponentName = "AddComponent";
	private const string CreateInstanceName = "CreateInstance";

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

	protected override void OnCompilationStart(CompilationStartAnalysisContext context, CompilationScope scope)
	{
		// Built on the first string lookup only, since it walks every type the game can see.
		var typesByName = new Lazy<UnityTypesByName>(() => UnityTypesByName.Collect(context.Compilation));
		context.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(nodeContext => AnalyzeInvocation(nodeContext, typesByName)), SyntaxKind.InvocationExpression);
	}

	private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, Lazy<UnityTypesByName> typesByName)
	{
		var invocation = (InvocationExpressionSyntax)context.Node;
		var name = invocation.Expression is MemberBindingExpressionSyntax binding ? binding.Name : invocation.GetMethodNameSyntax();
		if (name == null || name is GenericNameSyntax || invocation.ArgumentList.Arguments.Count == 0)
			return;

		var methodName = name.Identifier.Text;
		if (!KnownMethods.IsGetComponentName(name) && methodName != AddComponentName && methodName != CreateInstanceName)
			return;

		if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method || !HasGenericOverload(method))
			return;

		var argument = invocation.ArgumentList.Arguments[0].Expression;
		var callTail = invocation.ArgumentList.Arguments.Count > 1 ? "(...)" : "()";

		if (method.Parameters[0].Type.Matches(typeof(Type)))
		{
			if (argument is not TypeOfExpressionSyntax typeOf || !FitsGenericForm(method, context.SemanticModel.GetTypeInfo(typeOf.Type, context.CancellationToken).Type))
				return;

			Report(context, invocation, $"{method.Name}(typeof({typeOf.Type})) has a generic overload", $"{method.Name}<{typeOf.Type}>{callTail}");
			return;
		}

		if (context.SemanticModel.GetConstantValue(argument, context.CancellationToken).Value is not string typeName)
			return;

		var forScriptableObject = method.Name == CreateInstanceName;
		var matches = typesByName.Value.Named(typeName, forScriptableObject);
		var call = $"{method.Name}(\"{typeName}\")";
		if (matches.Count == 0)
			Report(context, invocation, $"{call} names no {(forScriptableObject ? "ScriptableObject" : "component")} type, so it returns null", "the generic overload");
		else if (matches.Count == 1)
			Report(context, invocation, $"{call} looks the type up by name when the game runs", $"{method.Name}<{matches[0].ToMinimalDisplayString(context.SemanticModel, invocation.SpanStart)}>{callTail}");
		else
			Report(context, invocation, $"{call} looks the type up by name when the game runs", "the generic overload with the type you mean");
	}

	private static void Report(SyntaxNodeAnalysisContext context, InvocationExpressionSyntax invocation, string problem, string fix)
	{
		context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), problem, fix));
	}

	// GetComponent and its relatives taking a Type or a string, GameObject.AddComponent(Type) and
	// ScriptableObject.CreateInstance(Type or string).
	private static bool HasGenericOverload(IMethodSymbol method)
	{
		if (method.IsGenericMethod || method.Parameters.Length == 0)
			return false;

		var firstParameter = method.Parameters[0].Type;
		var takesTypeOrName = firstParameter.Matches(typeof(Type)) || firstParameter.SpecialType == SpecialType.System_String;
		if (KnownMethods.IsGetComponent(method))
			return takesTypeOrName;

		if (method.Name == AddComponentName)
			return method.ContainingType.Matches(typeof(UnityEngine.GameObject)) && firstParameter.Matches(typeof(Type));

		return method.Name == CreateInstanceName && method.ContainingType.Matches(typeof(UnityEngine.ScriptableObject)) && takesTypeOrName;
	}

	// AddComponent<T> and CreateInstance<T> constrain T, so typeof of anything else has no generic form.
	private static bool FitsGenericForm(IMethodSymbol method, ITypeSymbol? type)
	{
		var constraint = method.Name switch
		{
			AddComponentName => typeof(UnityEngine.Component),
			CreateInstanceName => typeof(UnityEngine.ScriptableObject),
			_ => null
		};

		if (constraint == null)
			return true;

		if (type is ITypeParameterSymbol typeParameter)
			return typeParameter.ConstraintTypes.Any(candidate => candidate.Extends(constraint));

		return type != null && type.Extends(constraint);
	}

	// Component and ScriptableObject types by short and full name, for string lookups.
	private sealed class UnityTypesByName
	{
		private readonly Dictionary<string, List<INamedTypeSymbol>> _components = new(StringComparer.Ordinal);
		private readonly Dictionary<string, List<INamedTypeSymbol>> _scriptableObjects = new(StringComparer.Ordinal);

		public static UnityTypesByName Collect(Compilation compilation)
		{
			var types = new UnityTypesByName();
			var componentType = compilation.GetTypeByMetadataName(typeof(UnityEngine.Component).FullName!);
			var scriptableObjectType = compilation.GetTypeByMetadataName(typeof(UnityEngine.ScriptableObject).FullName!);
			if (componentType != null || scriptableObjectType != null)
				types.AddFrom(compilation.GlobalNamespace, componentType, scriptableObjectType);
			return types;
		}

		public IReadOnlyList<INamedTypeSymbol> Named(string name, bool scriptableObject)
		{
			var byName = scriptableObject ? _scriptableObjects : _components;
			return byName.TryGetValue(name, out var found) ? found : Array.Empty<INamedTypeSymbol>();
		}

		private void AddFrom(INamespaceOrTypeSymbol container, INamedTypeSymbol? componentType, INamedTypeSymbol? scriptableObjectType)
		{
			foreach (var member in container.GetMembers())
			{
				if (member is INamespaceSymbol childNamespace)
				{
					AddFrom(childNamespace, componentType, scriptableObjectType);
					continue;
				}

				if (member is not INamedTypeSymbol type)
					continue;

				if (type.Arity == 0)
				{
					if (DerivesFrom(type, componentType))
						Add(_components, type);
					else if (DerivesFrom(type, scriptableObjectType))
						Add(_scriptableObjects, type);
				}

				AddFrom(type, componentType, scriptableObjectType);
			}
		}

		private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
		{
			if (baseType == null)
				return false;

			for (var current = type; current != null; current = current.BaseType)
			{
				if (SymbolEqualityComparer.Default.Equals(current, baseType))
					return true;
			}

			return false;
		}

		private static void Add(Dictionary<string, List<INamedTypeSymbol>> byName, INamedTypeSymbol type)
		{
			foreach (var key in new[] { type.Name, type.ToDisplayString() })
			{
				if (!byName.TryGetValue(key, out var list))
					byName[key] = list = new List<INamedTypeSymbol>();
				if (!list.Contains(type, SymbolEqualityComparer.Default))
					list.Add(type);
			}
		}
	}
}