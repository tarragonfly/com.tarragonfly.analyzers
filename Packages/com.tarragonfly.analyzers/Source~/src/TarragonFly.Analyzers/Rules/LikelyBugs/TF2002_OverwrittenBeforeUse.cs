using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
    // Reported when the first use, in the order the code runs, is a plain assignment and no path reads
    // the value passed in. A write inside an if, or in a loop that may not run, is fine when the code
    // after it reads the variable. "x = x" is always reported. Reads such as "x = x + 1", "x += 1" and
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
                    if (FirstUse(body, parameter) is IOperation use && IsOverwrite(use) && !MayReadBeforeWriting(body, use, parameter)) context.ReportDiagnostic(Diagnostic.Create(Rule, use.Syntax.GetLocation(), parameter.Name));
                }
            }, OperationKind.MethodBody, OperationKind.ConstructorBody, OperationKind.AnonymousFunction, OperationKind.LocalFunction);

            start.RegisterOperationAction(context =>
            {
                ICatchClauseOperation clause = (ICatchClauseOperation)context.Operation;
                if (!scope.IsAnalyzed(clause.Syntax.SyntaxTree) || !(clause.ExceptionDeclarationOrExpression is IVariableDeclaratorOperation declarator)) return;
                if (clause.Filter != null && FirstUse(clause.Filter, declarator.Symbol) != null) return;
                if (FirstUse(clause.Handler, declarator.Symbol) is IOperation use && IsOverwrite(use) && !MayReadBeforeWriting(clause.Handler, use, declarator.Symbol)) context.ReportDiagnostic(Diagnostic.Create(Rule, use.Syntax.GetLocation(), declarator.Symbol.Name));
            }, OperationKind.CatchClause);
        }

        private static bool IsOverwrite(IOperation use)
        {
            return use.Parent is ISimpleAssignmentOperation assignment && assignment.Target == use;
        }

        // True when some path reads the variable before writing it, as in "if (b) x = 3; return x;".
        // "x = x" only reads itself, so it doesn't count. If the compiler can't analyze the body,
        // assume the value is read.
        private static bool MayReadBeforeWriting(IOperation body, IOperation use, ISymbol variable)
        {
            if (use.Parent is ISimpleAssignmentOperation assignment && IsVariable(Unwrap(assignment.Value), variable)) return false;
            SemanticModel model = use.SemanticModel;
            if (model == null) return true;
            if (!(body is IMethodBodyBaseOperation member)) return MayRead(model, body.Syntax, variable);

            SyntaxNode memberBody = member.BlockBody?.Syntax ?? member.ExpressionBody?.Syntax;
            // ": base(x = 5)" runs before the body. If that write only happens sometimes, the body can
            // still read the value passed in, so check the body too.
            if (member.Syntax is ConstructorDeclarationSyntax { Initializer: ConstructorInitializerSyntax initializer } && initializer.Span.Contains(use.Syntax.Span))
            {
                DataFlowAnalysis flow = model.AnalyzeDataFlow(initializer);
                if (flow == null || !flow.Succeeded || flow.DataFlowsIn.Contains(variable, SymbolEqualityComparer.Default)) return true;
                return !flow.AlwaysAssigned.Contains(variable, SymbolEqualityComparer.Default) && memberBody != null && MayRead(model, memberBody, variable);
            }
            return MayRead(model, memberBody, variable);
        }

        private static bool MayRead(SemanticModel model, SyntaxNode code, ISymbol variable)
        {
            DataFlowAnalysis flow;
            switch (code)
            {
                case StatementSyntax statement:
                    flow = model.AnalyzeDataFlow(statement);
                    break;
                case ArrowExpressionClauseSyntax arrow:
                    flow = model.AnalyzeDataFlow(arrow.Expression);
                    break;
                case ExpressionSyntax expression:
                    flow = model.AnalyzeDataFlow(expression);
                    break;
                default:
                    return true;
            }
            return flow == null || !flow.Succeeded || flow.DataFlowsIn.Contains(variable, SymbolEqualityComparer.Default);
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
