using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules
{
    // The branches of if/else-if chains and switch statements, and whether two of them hold the same code.
    public static class Branches
    {
        // The first if of a chain; "else if" is part of the chain that starts above it.
        public static bool IsChainStart(IfStatementSyntax ifStatement)
        {
            return !(ifStatement.Parent is ElseClauseSyntax);
        }

        // The body of every branch in order, the final else included.
        public static List<StatementSyntax> OfIfChain(IfStatementSyntax first, out bool endsWithElse)
        {
            List<StatementSyntax> branches = new List<StatementSyntax>();
            IfStatementSyntax current = first;
            endsWithElse = false;
            while (current != null)
            {
                branches.Add(current.Statement);
                ElseClauseSyntax elseClause = current.Else;
                if (elseClause == null) break;
                current = elseClause.Statement as IfStatementSyntax;
                if (current == null)
                {
                    branches.Add(elseClause.Statement);
                    endsWithElse = true;
                }
            }
            return branches;
        }

        // A branch body as a list of statements: a block's statements, or the single unbraced statement.
        public static IReadOnlyList<StatementSyntax> Statements(StatementSyntax body)
        {
            return body is BlockSyntax block ? (IReadOnlyList<StatementSyntax>)block.Statements : new[] { body };
        }

        // Same code ignoring comments, whitespace and line breaks.
        public static bool HaveSameCode(IReadOnlyList<StatementSyntax> first, IReadOnlyList<StatementSyntax> second)
        {
            if (first.Count != second.Count) return false;
            for (int i = 0; i < first.Count; i++)
            {
                if (!SyntaxFactory.AreEquivalent(first[i], second[i], topLevel: false)) return false;
            }
            return true;
        }

        public static bool HasDefaultLabel(SwitchSectionSyntax section)
        {
            foreach (SwitchLabelSyntax label in section.Labels)
            {
                if (label.IsKind(SyntaxKind.DefaultSwitchLabel)) return true;
            }
            return false;
        }
    }
}