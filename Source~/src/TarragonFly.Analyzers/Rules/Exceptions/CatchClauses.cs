using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TarragonFly.Analyzers.Rules.Exceptions
{
    public static class CatchClauses
    {
        // Whether a catch of this try, without a filter, takes exceptions of the thrown type.
        // A catch-all counts; an unknown thrown type only matches a catch-all.
        public static bool Catch(SemanticModel model, TryStatementSyntax tryStatement, ITypeSymbol thrown)
        {
            foreach (CatchClauseSyntax catchClause in tryStatement.Catches)
            {
                if (catchClause.Filter != null) continue;
                if (catchClause.Declaration == null) return true;
                ITypeSymbol caught = model.GetTypeInfo(catchClause.Declaration.Type).Type;
                for (ITypeSymbol current = thrown; current != null; current = current.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(current, caught)) return true;
                }
            }
            return false;
        }
    }
}