/*--------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *  Changed for Tarragon.Fly() Unity Analyzers: TF rule IDs, analyzed-folder scoping, no code fixes.
 *-------------------------------------------------------------------------------------------*/

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.Unity;

internal static class EditorDirectives
{
	private static readonly HashSet<string> EditorDefines = new()
	{
		"UNITY_EDITOR", "UNITY_EDITOR_WIN", "UNITY_EDITOR_OSX", "UNITY_EDITOR_LINUX", "UNITY_EDITOR_64",
	};

	public static bool IsEditorOnly(SyntaxNode node)
	{
		var syntaxTree = node.SyntaxTree;
		var position = node.SpanStart;

		var root = syntaxTree.GetRoot();
		var directives = root.DescendantTrivia()
			.Where(t => t.SpanStart < position &&
					   (t.IsKind(SyntaxKind.IfDirectiveTrivia) ||
						t.IsKind(SyntaxKind.ElifDirectiveTrivia) ||
						t.IsKind(SyntaxKind.ElseDirectiveTrivia) ||
						t.IsKind(SyntaxKind.EndIfDirectiveTrivia)))
			.OrderBy(t => t.SpanStart)
			.ToList();

		var stack = new Stack<(bool? CompiledOutsideEditor, bool? LaterBranchCanRun)>();

		foreach (var directive in directives)
		{
			switch (directive.Kind())
			{
				case SyntaxKind.IfDirectiveTrivia:
					bool? condition = EvaluateOutsideEditor(((IfDirectiveTriviaSyntax)directive.GetStructure()!).Condition);
					stack.Push((condition, !condition));
					break;
				case SyntaxKind.ElifDirectiveTrivia:
					if (stack.Count == 0) break;
					var previous = stack.Pop();
					bool? alternative = EvaluateOutsideEditor(((ElifDirectiveTriviaSyntax)directive.GetStructure()!).Condition);
					stack.Push((previous.LaterBranchCanRun & alternative, previous.LaterBranchCanRun & !alternative));
					break;
				case SyntaxKind.ElseDirectiveTrivia:
					if (stack.Count <= 0)
						continue;

					stack.Push((stack.Pop().LaterBranchCanRun, false));
					break;
				case SyntaxKind.EndIfDirectiveTrivia:
					if (stack.Count > 0)
						stack.Pop();
					break;
			}
		}

		return stack.Any(branch => branch.CompiledOutsideEditor == false);
	}

	private static bool? EvaluateOutsideEditor(ExpressionSyntax condition)
	{
		return condition switch
		{
			IdentifierNameSyntax idName when EditorDefines.Contains(idName.Identifier.ValueText) => false,
			LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
			LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => false,
			PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } prefixUnary => !EvaluateOutsideEditor(prefixUnary.Operand),
			ParenthesizedExpressionSyntax paren => EvaluateOutsideEditor(paren.Expression),
			BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalOrExpression } binary => EvaluateOutsideEditor(binary.Left) | EvaluateOutsideEditor(binary.Right),
			BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalAndExpression } binary => EvaluateOutsideEditor(binary.Left) & EvaluateOutsideEditor(binary.Right),
			BinaryExpressionSyntax { RawKind: (int)SyntaxKind.EqualsExpression } binary => Compare(EvaluateOutsideEditor(binary.Left), EvaluateOutsideEditor(binary.Right)),
			BinaryExpressionSyntax { RawKind: (int)SyntaxKind.NotEqualsExpression } binary => !Compare(EvaluateOutsideEditor(binary.Left), EvaluateOutsideEditor(binary.Right)),
			_ => null
		};
	}

	private static bool? Compare(bool? left, bool? right)
	{
		return left.HasValue && right.HasValue ? left.Value == right.Value : null;
	}
}
