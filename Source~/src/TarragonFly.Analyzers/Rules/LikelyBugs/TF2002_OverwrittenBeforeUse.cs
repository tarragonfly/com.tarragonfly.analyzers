using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TarragonFly.Analyzers.Rules.LikelyBugs
{
    // TF2002: a parameter or catch variable that is assigned before the code ever reads it, as in "void
    // Heal(int amount) { amount = maxHealth; ... }".
    //
    // The caller's value, or the caught exception, is thrown away unread, which is almost always a
    // mistake: another variable was meant, or the parameter isn't needed.
    //
    // The first use in the order the code runs decides: when it is a plain assignment, it is reported,
    // also when it sits in an if or a loop, and so is "x = x". Reads such as "x = x + 1", "x += 1" and
    // "x ??= y" are fine, as are out and ref parameters. Lambda parameters count; a use inside a lambda
    // counts as a read for the code around it. Reported on the assigned name.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OverwrittenBeforeUseAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF2002",
            "Parameter or catch variable overwritten before use",
            "'{0}' is overwritten before anything reads it, so the value passed in is lost",
            "LikelyBugs",
            CodeKind.All);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            start.RegisterOperationAction(context =>
            {
                if (!scope.IsAnalyzed(context.Operation.Syntax.SyntaxTree)) return;
                IMethodSymbol method;
                IOperation body;
                switch (context.Operation)
                {
                    case IMethodBodyBaseOperation member when context.ContainingSymbol is IMethodSymbol owner:
                        method = owner;
                        body = member;
                        break;
                    case IAnonymousFunctionOperation lambda:
                        method = lambda.Symbol;
                        body = lambda.Body;
                        break;
                    case ILocalFunctionOperation local:
                        method = local.Symbol;
                        body = local.Body;
                        break;
                    default:
                        return;
                }
                foreach (IParameterSymbol parameter in method.Parameters)
                {
                    if (parameter.RefKind != RefKind.None) continue;
                    if (FirstUse(body, parameter) is IOperation use && IsOverwrite(use)) context.ReportDiagnostic(Diagnostic.Create(Rule, use.Syntax.GetLocation(), parameter.Name));
                }
            }, OperationKind.MethodBody, OperationKind.ConstructorBody, OperationKind.AnonymousFunction, OperationKind.LocalFunction);

            start.RegisterOperationAction(context =>
            {
                ICatchClauseOperation clause = (ICatchClauseOperation)context.Operation;
                if (!scope.IsAnalyzed(clause.Syntax.SyntaxTree) || !(clause.ExceptionDeclarationOrExpression is IVariableDeclaratorOperation declarator)) return;
                if (FirstUse(clause.Handler, declarator.Symbol) is IOperation use && IsOverwrite(use)) context.ReportDiagnostic(Diagnostic.Create(Rule, use.Syntax.GetLocation(), declarator.Symbol.Name));
            }, OperationKind.CatchClause);
        }

        private static bool IsOverwrite(IOperation use)
        {
            return use.Parent is ISimpleAssignmentOperation assignment && assignment.Target == use;
        }

        private static IOperation Unwrap(IOperation value)
        {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            return value;
        }

        private static bool IsVariable(IOperation operation, ISymbol variable)
        {
            return (operation is IParameterReferenceOperation parameter && SymbolEqualityComparer.Default.Equals(parameter.Parameter, variable))
                || (operation is ILocalReferenceOperation local && SymbolEqualityComparer.Default.Equals(local.Local, variable));
        }

        // The first reference to the variable in the order the code runs, or null. A lambda or local
        // function that mentions it counts as a read, returned as the function itself.
        private static IOperation FirstUse(IOperation operation, ISymbol variable)
        {
            switch (operation)
            {
                case IParameterReferenceOperation parameter when SymbolEqualityComparer.Default.Equals(parameter.Parameter, variable):
                    return parameter;
                case ILocalReferenceOperation local when SymbolEqualityComparer.Default.Equals(local.Local, variable):
                    return local;
                case ISimpleAssignmentOperation assignment:
                    if (IsVariable(Unwrap(assignment.Value), variable) && IsVariable(assignment.Target, variable)) return assignment.Target;
                    return FirstUse(assignment.Value, variable) ?? FirstUse(assignment.Target, variable);
                case IAnonymousFunctionOperation _:
                case ILocalFunctionOperation _:
                    foreach (IOperation inner in operation.Descendants())
                    {
                        if (inner is IParameterReferenceOperation p && SymbolEqualityComparer.Default.Equals(p.Parameter, variable)) return operation;
                        if (inner is ILocalReferenceOperation l && SymbolEqualityComparer.Default.Equals(l.Local, variable)) return operation;
                    }
                    return null;
            }
            foreach (IOperation child in operation.ChildOperations)
            {
                IOperation found = FirstUse(child, variable);
                if (found != null) return found;
            }
            return null;
        }
    }
}