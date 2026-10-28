using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TarragonFly.Analyzers.Rules.Readability
{
    // TF1025: a local, loop, using or out variable declared with 'var', as in "var enemy = Spawn();"
    // where "Enemy enemy = Spawn();" says what came back.
    //
    // With the type written out, a diff, a review or an agent reading one file knows what every
    // variable holds without opening the method it came from. Long generic types get noisy; that's
    // the accepted price, so the rule is off until the project turns it on.
    //
    // Off by default: report_var = game reports runtime and editor assemblies, all reports test
    // assemblies too. A type that can't be written, because it is or contains an anonymous type, is
    // never reported. Neither is a var whose line already names the type: a cast, an 'as', default(T),
    // an array made with its element type, a static member of the type such as
    // CancellationTokenSource.CreateLinkedTokenSource or Singleton<AudioManager>.Instance, or a call
    // whose type argument is what it returns, such as GetComponent<RectTransform>(), with or without
    // ?., an await on LoadAssetAsync<Sprite>() or a foreach over GetComponentsInChildren<Button>().
    // "var enemy = new Enemy()" is still reported, since "Enemy enemy = new()" says it once.
    // Not reported: a deconstruction such as "var (a, b) = pair", a pattern such as "is var x",
    // "out var _" and a type that happens to be named var. Reported on the 'var' keyword.
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class VarInsteadOfTypeAnalyzer : ScopedAnalyzer
    {
        public static readonly DiagnosticDescriptor Rule = Descriptors.Create(
            "TF1025",
            "'var' instead of the type name",
            "Write the type instead of 'var'",
            "Readability",
            CodeKind.All,
            description: "Off until report_var is set in the settings file: game reports runtime and editor code, all reports test code too. " +
                "A type that can't be written, such as an anonymous type, is never reported, and neither is a line that names the type already: " +
                "a cast, an 'as', an array made with its element type, a static member of the type or a call such as GetComponent<T>() that returns T.");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        protected override void OnCompilationStart(CompilationStartAnalysisContext start, CompilationScope scope)
        {
            if ((scope.Settings.ReportVarIn & scope.AssemblyKind) == 0) return;

            start.RegisterSyntaxNodeAction(scope.OnlyAnalyzedNodes(context =>
            {
                TypeSyntax type = DeclaredType(context.Node);
                if (type == null || !type.IsVar) return;
                // A type named var is written out already.
                if (context.SemanticModel.GetSymbolInfo(type, context.CancellationToken).Symbol is ITypeSymbol named && named.Name == "var") return;
                ITypeSymbol varType = context.SemanticModel.GetTypeInfo(type, context.CancellationToken).Type;
                if (!CanBeWritten(varType)) return;
                if (LineNamesType(context.Node, varType, context.SemanticModel, context.CancellationToken)) return;
                context.ReportDiagnostic(Diagnostic.Create(Rule, type.GetLocation()));
            }), SyntaxKind.VariableDeclaration, SyntaxKind.ForEachStatement, SyntaxKind.DeclarationExpression);
        }

        // The written type of a local, for, using, fixed, foreach or out declaration; null for the
        // declaration shapes the rule skips.
        private static TypeSyntax DeclaredType(SyntaxNode node)
        {
            switch (node)
            {
                case VariableDeclarationSyntax declaration:
                    return declaration.Type;
                case ForEachStatementSyntax loop:
                    return loop.Type;
                case DeclarationExpressionSyntax declaration:
                    bool isOutVariable = declaration.Parent is ArgumentSyntax argument && argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword);
                    return isOutVariable && declaration.Designation is SingleVariableDesignationSyntax ? declaration.Type : null;
                default:
                    return null;
            }
        }

        // True when the value or loop source already spells out the type. Writing it on the left too
        // would just repeat it.
        private static bool LineNamesType(SyntaxNode node, ITypeSymbol type, SemanticModel model, CancellationToken cancellation)
        {
            switch (node)
            {
                case VariableDeclarationSyntax declaration when declaration.Variables.Count == 1 && declaration.Variables[0].Initializer != null:
                    return ValueNamesType(declaration.Variables[0].Initializer.Value, type, model, cancellation);
                case ForEachStatementSyntax loop:
                    return ReturnsItsTypeArgument(loop.Expression, type, unwrapResult: true, model, cancellation);
                default:
                    return false;
            }
        }

        private static bool ValueNamesType(ExpressionSyntax value, ITypeSymbol type, SemanticModel model, CancellationToken cancellation)
        {
            value = Unwrapped(value);
            switch (value)
            {
                case ConditionalAccessExpressionSyntax conditional:
                    return ValueNamesType(conditional.WhenNotNull, type, model, cancellation);
                case AwaitExpressionSyntax awaited:
                    return ReturnsItsTypeArgument(awaited.Expression, type, unwrapResult: true, model, cancellation);
                case CastExpressionSyntax _:
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                case ArrayCreationExpressionSyntax _:
                case DefaultExpressionSyntax _:
                    return true;
                case InvocationExpressionSyntax invocation:
                    return ReturnsItsTypeArgument(invocation, type, unwrapResult: false, model, cancellation) || IsStaticMemberOf(invocation.Expression, type, model, cancellation);
                default:
                    return IsStaticMemberOf(value, type, model, cancellation);
            }
        }

        // Parentheses and the null-forgiving '!' leave the type just as visible, so "(Get<Wheel>())"
        // and "Get<Wheel>()!" count the same as "Get<Wheel>()".
        private static ExpressionSyntax Unwrapped(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        break;
                    case PostfixUnaryExpressionSyntax forgiven when forgiven.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                        expression = forgiven.Operand;
                        break;
                    default:
                        return expression;
                }
            }
        }

        // A call whose written type argument is what it returns, like GetComponent<T>() or
        // Resources.Load<T>(). With unwrapResult the variable holds the T inside the result: a foreach
        // over GetComponentsInChildren<T>() gets T out of T[], an await on LoadAssetAsync<T>() gets T out
        // of the handle. A plain var holding the T[] still gets reported.
        private static bool ReturnsItsTypeArgument(ExpressionSyntax expression, ITypeSymbol type, bool unwrapResult, SemanticModel model, CancellationToken cancellation)
        {
            if (!(Unwrapped(expression) is InvocationExpressionSyntax invocation)) return false;
            if (!(CalledName(invocation.Expression) is GenericNameSyntax generic)) return false;
            if (!(model.GetSymbolInfo(invocation, cancellation).Symbol is IMethodSymbol method)) return false;

            ITypeSymbol returnType = method.OriginalDefinition.ReturnType;
            if (unwrapResult) returnType = InnerType(returnType);
            if (!(returnType is ITypeParameterSymbol parameter) || parameter.TypeParameterKind != TypeParameterKind.Method) return false;

            SeparatedSyntaxList<TypeSyntax> arguments = generic.TypeArgumentList.Arguments;
            return parameter.Ordinal < arguments.Count
                && SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(arguments[parameter.Ordinal], cancellation).Type, type);
        }

        private static SimpleNameSyntax CalledName(ExpressionSyntax called)
        {
            switch (called)
            {
                case MemberAccessExpressionSyntax access:
                    return access.Name;
                case MemberBindingExpressionSyntax binding:
                    return binding.Name;
                default:
                    return called as SimpleNameSyntax;
            }
        }

        // The T in T[], IEnumerable<T>, UniTask<T> or AsyncOperationHandle<T>. Null for anything with
        // more than one argument, like Dictionary<TKey, TValue>, since there's no telling which comes out.
        private static ITypeSymbol InnerType(ITypeSymbol outer)
        {
            switch (outer)
            {
                case IArrayTypeSymbol array:
                    return array.ElementType;
                case INamedTypeSymbol named when named.TypeArguments.Length == 1:
                    return named.TypeArguments[0];
                default:
                    return null;
            }
        }

        // Vector3.zero, TimeSpan.FromSeconds(1), CancellationTokenSource.CreateLinkedTokenSource(token),
        // and Singleton<AudioManager>.Instance, where the type is the receiver's type argument.
        private static bool IsStaticMemberOf(ExpressionSyntax expression, ITypeSymbol type, SemanticModel model, CancellationToken cancellation)
        {
            if (!(expression is MemberAccessExpressionSyntax access)) return false;
            if (!(model.GetSymbolInfo(access.Expression, cancellation).Symbol is ITypeSymbol owner)) return false;
            if (SymbolEqualityComparer.Default.Equals(owner, type)) return true;
            if (!(owner is INamedTypeSymbol namedOwner)) return false;
            foreach (ITypeSymbol argument in namedOwner.TypeArguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument, type)) return true;
            }
            return false;
        }

        // False for an anonymous type and for anything built on one, such as an array of them, a
        // tuple holding one or the IEnumerable a Select into one returns.
        private static bool CanBeWritten(ITypeSymbol type)
        {
            switch (type)
            {
                case null:
                case IErrorTypeSymbol _:
                    return false;
                case IArrayTypeSymbol array:
                    return CanBeWritten(array.ElementType);
                case IPointerTypeSymbol pointer:
                    return CanBeWritten(pointer.PointedAtType);
                case INamedTypeSymbol named:
                    if (named.IsAnonymousType) return false;
                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        if (!CanBeWritten(argument)) return false;
                    }
                    return named.ContainingType == null || CanBeWritten(named.ContainingType);
                default:
                    return true;
            }
        }
    }
}
