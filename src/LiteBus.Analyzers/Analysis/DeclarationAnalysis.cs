using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers.Analysis;

/// <summary>
///     Shared logic for deciding whether a message states a position on one metadata value type.
/// </summary>
/// <remarks>
///     Both LB1018 and LB1020 ask the same question about a different value type, so the answer lives here once.
///     LB1018 is the preconfigured case for <c>AuditDeclaration</c>; LB1020 reads its value types from configuration.
/// </remarks>
internal static class DeclarationAnalysis
{
    /// <summary>
    ///     The <c>.editorconfig</c> key naming the metadata value types every message must declare.
    /// </summary>
    internal const string RequiredDeclarationsOption = "litebus_required_declarations";

    /// <summary>
    ///     The name of the method a describing definition declares its values from.
    /// </summary>
    private const string DescribeMethodName = "Describe";

    /// <summary>
    ///     The separators accepted between value type names in the configured list.
    /// </summary>
    private static readonly char[] NameSeparators = [',', ';'];

    /// <summary>
    ///     The message kinds a declaration requirement applies to.
    /// </summary>
    /// <remarks>
    ///     Events are excluded: event declaration requirements are composition-only, enforced by
    ///     <c>RequireDeclaration</c> at composition time rather than by <c>LB1020</c>.
    /// </remarks>
    private static readonly MessageKind[] DeclarableKinds =
        [MessageKind.Command, MessageKind.Query, MessageKind.StreamQuery];

    /// <summary>
    ///     Collects the declaration facts one source type carries.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <remarks>
    ///     Only types declared in the analyzed assembly are collected. A message from a referenced assembly cannot be
    ///     annotated from here, so reporting it would produce a warning with no available fix. Nested types need no
    ///     recursion: the symbol action fires for them too.
    /// </remarks>
    internal static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols)
    {
        if (context.Symbol is not INamedTypeSymbol type ||
            !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, context.Compilation.Assembly))
        {
            return;
        }

        CollectMessageCandidate(context, collector, symbols, type);
        CollectDefinitionFacts(collector, symbols, type);
        CollectDeclaringAttributeFact(collector, symbols, type);
    }

    /// <summary>
    ///     Records the type as a message candidate for every declarable kind it implements.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="type">The candidate type.</param>
    private static void CollectMessageCandidate(
        SymbolAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol type)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            type.IsAbstract ||
            HandlerAnalysis.IsGenericTypeDefinition(type))
        {
            return;
        }

        foreach (var kind in DeclarableKinds)
        {
            if (!ImplementsAnyMarker(type, symbols.MarkersFor(kind)))
            {
                continue;
            }

            if (kind == MessageKind.Query && ImplementsAnyMarker(type, symbols.StreamQueryMarkers))
            {
                continue;
            }

            var messageType = LiteBusSymbols.RetargetToCompilation(context.Compilation, type);
            var location = LiteBusSymbols.GetDiagnosticLocation(
                context.Compilation,
                messageType.Locations.FirstOrDefault(candidate => candidate.IsInSource) ??
                messageType.Locations.FirstOrDefault() ??
                Location.None);
            collector.Messages(kind).Add(new MessageTypeRegistration(messageType, location));
        }
    }

    /// <summary>
    ///     Records the definition contracts one source type implements.
    /// </summary>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="type">The candidate definition type.</param>
    /// <remarks>
    ///     The keyed shape names the value type in the contract, so the type arguments settle it. The Describe shape
    ///     names nothing in the contract, so what it declares is read out of the method body by a syntax node action
    ///     and matched to the definition at compilation end.
    /// </remarks>
    private static void CollectDefinitionFacts(
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol type)
    {
        if (symbols.DefinitionContract is null && symbols.DescribingContract is null)
        {
            return;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (!contract.IsGenericType)
            {
                continue;
            }

            if (symbols.DefinitionContract is not null &&
                SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.DefinitionContract) &&
                contract.TypeArguments.Length == 2 &&
                contract.TypeArguments[0] is INamedTypeSymbol described)
            {
                collector.KeyedDefinitions.Add((described, contract.TypeArguments[1]));
                continue;
            }

            if (symbols.DescribingContract is not null &&
                symbols.Declarations is not null &&
                SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, symbols.DescribingContract) &&
                contract.TypeArguments.Length == 1 &&
                contract.TypeArguments[0] is INamedTypeSymbol describedByBody)
            {
                collector.DescribingDefinitions.Add((type, describedByBody));
            }
        }
    }

    /// <summary>
    ///     Records the value type the type declares when it carries the declaration annotation.
    /// </summary>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="type">The candidate attribute class.</param>
    private static void CollectDeclaringAttributeFact(
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol type)
    {
        if (symbols.MessageDeclarationAnnotation is null)
        {
            return;
        }

        foreach (var attribute in type.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.MessageDeclarationAnnotation) ||
                attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol declared)
            {
                collector.SourceDeclaringAttributes.Add((type, declared));
            }
        }
    }

    /// <summary>
    ///     Determines whether the type implements one of the supplied marker interfaces.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <param name="markers">The resolved marker interfaces.</param>
    /// <returns><see langword="true" /> when a marker interface is implemented.</returns>
    private static bool ImplementsAnyMarker(INamedTypeSymbol type, ImmutableArray<INamedTypeSymbol> markers)
    {
        foreach (var marker in markers)
        {
            if (LiteBusSymbols.Implements(type, marker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Records what one <c>Describe</c> method body declares.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <remarks>
    ///     The method is filtered syntactically by name and parameter count before anything is bound, and bodies are
    ///     recorded for every matching method, not only for types implementing the describing contract, because a
    ///     definition can inherit its <c>Describe</c> from a base type that implements nothing.
    /// </remarks>
    internal static void AnalyzeDescribeMethod(
        SyntaxNodeAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols)
    {
        var declaration = (MethodDeclarationSyntax) context.Node;

        if (!string.Equals(declaration.Identifier.Text, DescribeMethodName, StringComparison.Ordinal) ||
            declaration.ParameterList.Parameters.Count != 1)
        {
            return;
        }

        var declarations = symbols.Declarations;

        if (declarations is null)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method ||
            method.Parameters.Length != 1 ||
            !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, declarations))
        {
            return;
        }

        RecordDescribeBody(context, collector, declarations, symbols, declaration, method);
    }

    /// <summary>
    ///     Reads one <c>Describe</c> body and records what it declares.
    /// </summary>
    /// <param name="context">The syntax node analysis context, carrying the body's semantic model.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="declarations">The <c>IMessageDeclarations</c> symbol whose calls are the declarations.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="declaration">The <c>Describe</c> method declaration.</param>
    /// <param name="method">The declared <c>Describe</c> method.</param>
    /// <remarks>
    ///     <c>Declare</c> states a value and <c>Exempt</c> states that the message deliberately has none, and a
    ///     requirement is answered either way, exactly as <c>[DeclarationExempt]</c> is. <c>Audited</c> and
    ///     <c>NotAudited</c> both key their declaration by <c>AuditDeclaration</c>, which is what makes
    ///     <c>NotAudited</c> answer the same question <c>Audited</c> does, and <c>Authorized</c> and
    ///     <c>AuthorizationExempt</c> both key theirs by <c>AuthorizationDeclaration</c> for the same reason. Audit
    ///     declarations live in the auditing package as extension methods on the collector, so calls on it are matched
    ///     through their original definition as well as through ordinary members.
    /// </remarks>
    private static void RecordDescribeBody(
        SyntaxNodeAnalysisContext context,
        DeclarationCollector collector,
        INamedTypeSymbol declarations,
        DeclarationSymbols symbols,
        MethodDeclarationSyntax declaration,
        IMethodSymbol method)
    {
        var declaredValues = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var statesAuditPosition = false;
        var passesCollector = false;
        var collectorParameter = method.Parameters[0];

        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (PassesCollector(invocation, context.SemanticModel, collectorParameter, context.CancellationToken))
            {
                passesCollector = true;
                break;
            }

            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol called ||
                !IsCallOnDeclarations(called, declarations))
            {
                continue;
            }

            switch (called.Name)
            {
                case "Declare":
                case "Exempt":
                    if (called.TypeArguments.Length == 1)
                    {
                        declaredValues.Add(called.TypeArguments[0]);
                    }

                    break;

                case "Audited":
                case "NotAudited":
                    statesAuditPosition = true;
                    break;

                case "Authorized":
                case "AuthorizationExempt":
                    if (symbols.AuthorizationDeclaration is not null)
                    {
                        declaredValues.Add(symbols.AuthorizationDeclaration);
                    }

                    break;
            }
        }

        collector.DescribeBodies.Add((
            method,
            declaredValues.ToImmutable(),
            statesAuditPosition,
            passesCollector,
            declaration.SyntaxTree,
            declaration.Span));
    }

    /// <summary>
    ///     Records what one catalog source <c>Describe</c> method body declares for each message it names.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <remarks>
    ///     The method is filtered syntactically by name and parameter count before anything is bound. Only a
    ///     <c>Describe</c> whose containing type implements the catalog source contract counts, so an unrelated
    ///     method of the same shape contributes nothing. Each explicit <c>Describe</c> and <c>Replace</c> call is
    ///     read where it stands; a catalog handed to a helper declares out of this pass's sight, and composition
    ///     still enforces what the helper declares at startup.
    /// </remarks>
    internal static void AnalyzeCatalogDescribeMethod(
        SyntaxNodeAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols)
    {
        var declaration = (MethodDeclarationSyntax) context.Node;

        if (!string.Equals(declaration.Identifier.Text, DescribeMethodName, StringComparison.Ordinal) ||
            declaration.ParameterList.Parameters.Count != 1)
        {
            return;
        }

        var catalog = symbols.DeclarationCatalog;

        if (catalog is null || symbols.CatalogSourceContract is null || symbols.Declarations is null)
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not IMethodSymbol method ||
            method.Parameters.Length != 1 ||
            !SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, catalog))
        {
            return;
        }

        if (method.ContainingType is null || !ImplementsCatalogSource(method.ContainingType, symbols.CatalogSourceContract))
        {
            return;
        }

        RecordCatalogBody(context, collector, symbols, declaration);
    }

    /// <summary>
    ///     Reads one catalog source body and records what each <c>Describe</c> call declares.
    /// </summary>
    /// <param name="context">The syntax node analysis context, carrying the body's semantic model.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="declaration">The source <c>Describe</c> method declaration.</param>
    private static void RecordCatalogBody(
        SyntaxNodeAnalysisContext context,
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        MethodDeclarationSyntax declaration)
    {
        foreach (var invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol called ||
                !IsCallOnCatalog(called, symbols.DeclarationCatalog!))
            {
                continue;
            }

            if (!string.Equals(called.Name, "Describe", StringComparison.Ordinal) &&
                !string.Equals(called.Name, "Replace", StringComparison.Ordinal))
            {
                continue;
            }

            var described = DescribedMessage(invocation, called, context.SemanticModel, context.CancellationToken);

            if (described is null)
            {
                continue;
            }

            var lambda = CatalogLambda(invocation);

            if (lambda is null)
            {
                collector.CatalogFacts.Add((described, [], StatesAuditPosition: false, PassesCollector: true));
                continue;
            }

            var (declaredValues, statesAuditPosition, passesCollector) = ReadCatalogLambda(
                lambda,
                context.SemanticModel,
                symbols,
                context.CancellationToken);

            collector.CatalogFacts.Add((described, declaredValues, statesAuditPosition, passesCollector));
        }
    }

    /// <summary>
    ///     Determines whether a type implements the catalog source contract.
    /// </summary>
    /// <param name="type">The candidate containing type.</param>
    /// <param name="contract">The catalog source contract symbol.</param>
    /// <returns><see langword="true" /> when the type is a declaration source.</returns>
    private static bool ImplementsCatalogSource(INamedTypeSymbol type, INamedTypeSymbol contract)
    {
        return type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, contract));
    }

    /// <summary>
    ///     Determines whether an invocation calls a member of the declaration catalog.
    /// </summary>
    /// <param name="called">The invoked method symbol.</param>
    /// <param name="catalog">The declaration catalog symbol.</param>
    /// <returns><see langword="true" /> when the call targets the declaration catalog.</returns>
    private static bool IsCallOnCatalog(IMethodSymbol called, INamedTypeSymbol catalog)
    {
        return called.ContainingType is not null &&
            SymbolEqualityComparer.Default.Equals(called.ContainingType.OriginalDefinition, catalog);
    }

    /// <summary>
    ///     Reads the message type one catalog <c>Describe</c> call describes.
    /// </summary>
    /// <param name="invocation">The <c>Describe</c> invocation.</param>
    /// <param name="called">The invoked method symbol.</param>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns>The described message type, or <see langword="null" /> when it cannot be read.</returns>
    /// <remarks>
    ///     The generic call names the message in its type argument. The type-valued call names it through
    ///     <c>typeof</c> as its first argument.
    /// </remarks>
    private static INamedTypeSymbol? DescribedMessage(
        InvocationExpressionSyntax invocation,
        IMethodSymbol called,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (called.TypeArguments.Length == 1 && called.TypeArguments[0] is INamedTypeSymbol generic)
        {
            return generic;
        }

        if (invocation.ArgumentList.Arguments.Count == 2)
        {
            var typeExpression = invocation.ArgumentList.Arguments[0].Expression;

            if (typeExpression is TypeOfExpressionSyntax typeOf &&
                model.GetTypeInfo(typeOf.Type, cancellationToken).Type is INamedTypeSymbol typeOfTarget)
            {
                return typeOfTarget;
            }

            var typeInfo = model.GetTypeInfo(typeExpression, cancellationToken);

            if (typeInfo.ConvertedType is INamedTypeSymbol converted)
            {
                return converted;
            }

            if (model.GetSymbolInfo(typeExpression, cancellationToken).Symbol is INamedTypeSymbol named)
            {
                return named;
            }
        }

        return null;
    }

    /// <summary>
    ///     Finds the declaration lambda one catalog <c>Describe</c> call carries.
    /// </summary>
    /// <param name="invocation">The <c>Describe</c> invocation.</param>
    /// <returns>The lambda, or <see langword="null" /> when the call passes a method group or delegates elsewhere.</returns>
    private static ExpressionSyntax? CatalogLambda(InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;

        if (arguments.Count == 0)
        {
            return null;
        }

        var expression = arguments[arguments.Count - 1].Expression;

        return expression is SimpleLambdaExpressionSyntax or ParenthesizedLambdaExpressionSyntax or AnonymousMethodExpressionSyntax
            ? expression
            : null;
    }

    /// <summary>
    ///     Reads one catalog <c>Describe</c> lambda and records what it declares.
    /// </summary>
    /// <param name="lambda">The declaration lambda.</param>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns>What the lambda declares, whether it states an audit position, and whether it passes the collector on.</returns>
    /// <remarks>
    ///     The lambda parameter is the same <c>IMessageDeclarations</c> a <c>Describe</c> body receives, so the same
    ///     calls count: <c>Declare</c> and <c>Exempt</c> name their value in the type argument, <c>Audited</c> and
    ///     <c>NotAudited</c> state the audit position, and <c>Authorized</c> and <c>AuthorizationExempt</c> state the
    ///     authorization position.
    /// </remarks>
    private static (ImmutableArray<ITypeSymbol> DeclaredValues, bool StatesAuditPosition, bool PassesCollector) ReadCatalogLambda(
        ExpressionSyntax lambda,
        SemanticModel model,
        DeclarationSymbols symbols,
        CancellationToken cancellationToken)
    {
        var declaredValues = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var statesAuditPosition = false;

        var collectorParameter = LambdaCollectorParameter(lambda, model, symbols.Declarations!, cancellationToken);

        foreach (var invocation in lambda.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (collectorParameter is not null && PassesLambdaCollector(invocation, model, collectorParameter, cancellationToken))
            {
                return (declaredValues.ToImmutable(), statesAuditPosition, PassesCollector: true);
            }

            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol called ||
                !IsCallOnDeclarations(called, symbols.Declarations!))
            {
                continue;
            }

            switch (called.Name)
            {
                case "Declare":
                case "Exempt":
                    if (called.TypeArguments.Length == 1)
                    {
                        declaredValues.Add(called.TypeArguments[0]);
                    }

                    break;

                case "Audited":
                case "NotAudited":
                    statesAuditPosition = true;
                    break;

                case "Authorized":
                case "AuthorizationExempt":
                    if (symbols.AuthorizationDeclaration is not null)
                    {
                        declaredValues.Add(symbols.AuthorizationDeclaration);
                    }

                    break;
            }
        }

        return (declaredValues.ToImmutable(), statesAuditPosition, PassesCollector: false);
    }

    /// <summary>
    ///     Finds the lambda parameter holding the declaration collector.
    /// </summary>
    /// <param name="lambda">The declaration lambda.</param>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="declarations">The declaration collector symbol.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns>The collector parameter symbol, or <see langword="null" /> when it cannot be bound.</returns>
    private static IParameterSymbol? LambdaCollectorParameter(
        ExpressionSyntax lambda,
        SemanticModel model,
        INamedTypeSymbol declarations,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(lambda, cancellationToken).Symbol is not IMethodSymbol lambdaSymbol)
        {
            return null;
        }

        foreach (var parameter in lambdaSymbol.Parameters)
        {
            if (SymbolEqualityComparer.Default.Equals(parameter.Type, declarations))
            {
                return parameter;
            }
        }

        return lambdaSymbol.Parameters.Length == 1 ? lambdaSymbol.Parameters[0] : null;
    }

    /// <summary>
    ///     Determines whether an invocation inside a catalog <c>Describe</c> lambda hands the collector to something
    ///     else.
    /// </summary>
    /// <param name="invocation">The invocation in the lambda.</param>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="collector">The lambda parameter holding the collector.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns><see langword="true" /> when the collector is passed as an argument.</returns>
    private static bool PassesLambdaCollector(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        IParameterSymbol collector,
        CancellationToken cancellationToken)
    {
        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol called &&
            called.ContainingType is not null &&
            called.Name is "Describe" or "Replace")
        {
            return false;
        }

        return PassesCollector(invocation, model, collector, cancellationToken);
    }

    /// <summary>
    ///     Collects the required declaration names configured for one syntax tree.
    /// </summary>
    /// <param name="context">The syntax tree analysis context.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <remarks>
    ///     An <c>.editorconfig</c> entry under <c>[*.cs]</c> is attached to each syntax tree. The names are ordered and
    ///     deduplicated at compilation end, where the tree order is known.
    /// </remarks>
    internal static void CollectTreeOptions(SyntaxTreeAnalysisContext context, DeclarationCollector collector)
    {
        if (context.Options.AnalyzerConfigOptionsProvider.GetOptions(context.Tree)
            .TryGetValue(RequiredDeclarationsOption, out var value))
        {
            collector.TreeOptions.Add((context.Tree, SplitConfiguredNames(value)));
        }
    }

    /// <summary>
    ///     Splits one configured value into metadata names.
    /// </summary>
    /// <param name="value">The raw configuration value.</param>
    /// <returns>The trimmed, non-empty names in configuration order.</returns>
    internal static string[] SplitConfiguredNames(string value)
    {
        List<string> names = [];

        foreach (var part in value.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Trim();

            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return [.. names];
    }

    /// <summary>
    ///     Merges the global and per-tree configured value type names.
    /// </summary>
    /// <param name="globalNames">The names from the global options.</param>
    /// <param name="collector">The collector holding the per-tree names.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The configured metadata names, deduplicated and in configuration order.</returns>
    /// <remarks>
    ///     Global options come from a <c>.globalconfig</c> or a compiler-visible MSBuild property, while an
    ///     <c>.editorconfig</c> entry under <c>[*.cs]</c> is attached to each syntax tree. Both are read, because a
    ///     project-wide rule is natural to write in either place and a rule that silently ignores one of them looks
    ///     broken. Global names come first, then per-tree names in tree order.
    /// </remarks>
    internal static List<string> MergeConfiguredNames(
        string[] globalNames,
        DeclarationCollector collector,
        Compilation compilation)
    {
        List<string> names = [];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in globalNames)
        {
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        var order = BuildTreeOrder(compilation);

        foreach (var entry in collector.TreeOptions.OrderBy(candidate =>
                     order.TryGetValue(candidate.Tree, out var index) ? index : int.MaxValue))
        {
            foreach (var name in entry.Names)
            {
                if (seen.Add(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    /// <summary>
    ///     Maps each syntax tree to its index in the compilation, for ordering gathered facts.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>The tree index map.</returns>
    /// <remarks>
    ///     This is the one place that reads the compilation's syntax trees, and it runs only inside the
    ///     compilation-end action, where the read binds nothing and only orders facts the actions already gathered.
    /// </remarks>
    private static Dictionary<SyntaxTree, int> BuildTreeOrder(Compilation compilation)
    {
        var order = new Dictionary<SyntaxTree, int>();
        var index = 0;

        foreach (var tree in compilation.SyntaxTrees)
        {
            order[tree] = index;
            index++;
        }

        return order;
    }

    /// <summary>
    ///     Orders message candidates the way the source walk discovered them: by tree, then by position.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="registrations">The candidates for one message kind.</param>
    /// <returns>The candidates in discovery order.</returns>
    private static List<MessageTypeRegistration> OrderByDiscovery(
        Compilation compilation,
        ConcurrentBag<MessageTypeRegistration> registrations)
    {
        var order = BuildTreeOrder(compilation);

        return registrations
            .OrderBy(candidate => candidate.Location.SourceTree is not null &&
                order.TryGetValue(candidate.Location.SourceTree, out var index)
                    ? index
                    : int.MaxValue)
            .ThenBy(candidate => candidate.Location.IsInSource ? candidate.Location.SourceSpan.Start : 0)
            .ToList();
    }

    /// <summary>
    ///     Collects the message types in the analyzed assembly that state no position on the given value type.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="valueType">The metadata value type the messages must declare or be exempt from.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The offending message registrations, in discovery order.</returns>
    /// <remarks>
    ///     Only types declared in the analyzed assembly are reported. A message from a referenced assembly cannot be
    ///     annotated from here, so reporting it would produce a warning with no available fix.
    /// </remarks>
    internal static ImmutableArray<MessageTypeRegistration> CollectUndeclared(
        Compilation compilation,
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol valueType,
        CancellationToken cancellationToken)
    {
        var declaredByDefinition = CollectDefinedMessageTypes(collector, symbols, valueType, cancellationToken);
        var declaringAttributes =
            CollectDeclaringAttributes(compilation, collector, symbols, valueType, cancellationToken);

        var builder = ImmutableArray.CreateBuilder<MessageTypeRegistration>();

        foreach (var kind in DeclarableKinds)
        {
            foreach (var message in OrderByDiscovery(compilation, collector.Messages(kind)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (message.MessageType.IsAbstract ||
                    !SymbolEqualityComparer.Default.Equals(message.MessageType.ContainingAssembly, compilation.Assembly))
                {
                    continue;
                }

                if (CoveredByDefinition(message.MessageType, declaredByDefinition) ||
                    HasDeclaringAttribute(message.MessageType, declaringAttributes) ||
                    HasExemption(message.MessageType, valueType, symbols.DeclarationExempt))
                {
                    continue;
                }

                builder.Add(message);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Collects the message types a definition or a catalog source in the analyzed assembly declares the value
    ///     type for.
    /// </summary>
    /// <param name="collector">The collector holding the compilation's facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The message types covered by a definition or a source.</returns>
    private static ImmutableHashSet<INamedTypeSymbol> CollectDefinedMessageTypes(
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol valueType,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var keyed in collector.KeyedDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SymbolEqualityComparer.Default.Equals(keyed.Value, valueType))
            {
                builder.Add(keyed.Described);
            }
        }

        foreach (var describing in collector.DescribingDefinitions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DescribeDeclares(describing.Definition, valueType, symbols, collector, cancellationToken))
            {
                builder.Add(describing.Described);
            }
        }

        foreach (var catalog in collector.CatalogFacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (CatalogDeclares(catalog, valueType, symbols))
            {
                builder.Add(catalog.Described);
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Determines whether one catalog <c>Describe</c> call declares the required value type.
    /// </summary>
    /// <param name="fact">The recorded <c>Describe</c> call.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <returns><see langword="true" /> when the call covers the message for that value type.</returns>
    /// <remarks>
    ///     A call that passes the collector to another method or whose lambda cannot be read is treated as covered,
    ///     for the same reason an unreadable <c>Describe</c> body is: a false negative is still enforced at startup,
    ///     while a false positive is a build that cannot be made to pass.
    /// </remarks>
    private static bool CatalogDeclares(
        (INamedTypeSymbol Described, ImmutableArray<ITypeSymbol> DeclaredValues, bool StatesAuditPosition, bool PassesCollector) fact,
        INamedTypeSymbol valueType,
        DeclarationSymbols symbols)
    {
        if (fact.PassesCollector)
        {
            return true;
        }

        if (fact.DeclaredValues.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, valueType)))
        {
            return true;
        }

        return fact.StatesAuditPosition &&
            symbols.AuditDeclaration is not null &&
            SymbolEqualityComparer.Default.Equals(valueType, symbols.AuditDeclaration);
    }

    /// <summary>
    ///     Reads a definition's <c>Describe</c> body to decide whether it declares the required value type.
    /// </summary>
    /// <param name="definition">The definition type implementing the describing contract.</param>
    /// <param name="valueType">The value type a requirement asks every message to declare.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="collector">The collector holding the recorded <c>Describe</c> bodies.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns>
    ///     <see langword="true" /> when the body declares the value type, or when the body cannot be read at all.
    /// </returns>
    /// <remarks>
    ///     Unreadable is treated as declared, deliberately. This analyzer's failure mode matters: a false negative is
    ///     a rule the composition check still enforces at startup, while a false positive is a build that cannot be
    ///     made to pass without turning the rule off. So a body in a referenced assembly, an implementation this pass
    ///     cannot find, and a body that hands the collector to another method are all treated as covering the message.
    /// </remarks>
    private static bool DescribeDeclares(
        INamedTypeSymbol definition,
        INamedTypeSymbol valueType,
        DeclarationSymbols symbols,
        DeclarationCollector collector,
        CancellationToken cancellationToken)
    {
        if (symbols.Declarations is null)
        {
            return true;
        }

        var describe = definition
            .GetMembers(DescribeMethodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 1 &&
                                      SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, symbols.Declarations));

        var reference = describe?.DeclaringSyntaxReferences.FirstOrDefault();

        if (describe is null || reference is null)
        {
            return true;
        }

        var syntax = reference.GetSyntax(cancellationToken);

        foreach (var body in collector.DescribeBodies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!MethodMatches(body.Method, describe) ||
                !ReferenceEquals(body.Tree, syntax.SyntaxTree) ||
                body.Span != syntax.Span)
            {
                continue;
            }

            if (body.PassesCollector)
            {
                return true;
            }

            if (body.DeclaredValues.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, valueType)))
            {
                return true;
            }

            return body.StatesAuditPosition &&
                   symbols.AuditDeclaration is not null &&
                   SymbolEqualityComparer.Default.Equals(valueType, symbols.AuditDeclaration);
        }

        return true;
    }

    /// <summary>
    ///     Determines whether a recorded body belongs to the <c>Describe</c> method the definition resolves to.
    /// </summary>
    /// <param name="recorded">The method the body was recorded from.</param>
    /// <param name="resolved">The method the definition resolves to.</param>
    /// <returns><see langword="true" /> when the body is the resolved method's body.</returns>
    /// <remarks>
    ///     A definition can inherit <c>Describe</c> from a generic base, in which case the resolved method is the
    ///     constructed closing and the recorded one is the open definition; comparing original definitions keeps that
    ///     body readable.
    /// </remarks>
    private static bool MethodMatches(IMethodSymbol recorded, IMethodSymbol resolved)
    {
        return SymbolEqualityComparer.Default.Equals(recorded, resolved) ||
               SymbolEqualityComparer.Default.Equals(recorded.OriginalDefinition, resolved.OriginalDefinition);
    }

    /// <summary>
    ///     Determines whether a definition or a source declares the value for the message or for something it derives
    ///     from.
    /// </summary>
    /// <param name="messageType">The message type being checked.</param>
    /// <param name="declaredByDefinition">The types a definition or a source declares the value for.</param>
    /// <returns><see langword="true" /> when a definition or a source covers the message.</returns>
    /// <remarks>
    ///     A declaration applies to the type it names and to every message assignable to it, so a definition or a
    ///     source written for a base command or a marker interface satisfies the requirement for the whole family.
    /// </remarks>
    private static bool CoveredByDefinition(
        INamedTypeSymbol messageType,
        ImmutableHashSet<INamedTypeSymbol> declaredByDefinition)
    {
        if (declaredByDefinition.IsEmpty)
        {
            return false;
        }

        if (declaredByDefinition.Contains(messageType))
        {
            return true;
        }

        for (var baseType = messageType.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (declaredByDefinition.Contains(baseType))
            {
                return true;
            }
        }

        return messageType.AllInterfaces.Any(declaredByDefinition.Contains);
    }

    /// <summary>
    ///     Determines whether the message carries an attribute annotated as declaring the value type.
    /// </summary>
    /// <param name="messageType">The message type being checked.</param>
    /// <param name="declaringAttributes">The attribute classes annotated as declaring the value type.</param>
    /// <returns><see langword="true" /> when the message carries one of them.</returns>
    private static bool HasDeclaringAttribute(
        INamedTypeSymbol messageType,
        ImmutableHashSet<INamedTypeSymbol> declaringAttributes)
    {
        return !declaringAttributes.IsEmpty
               && messageType.GetAttributes().Any(attribute =>
                   attribute.AttributeClass is not null && declaringAttributes.Contains(attribute.AttributeClass));
    }

    /// <summary>
    ///     Determines whether the message records an exemption from declaring the value type.
    /// </summary>
    /// <param name="messageType">The message type being checked.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="exemptAttribute">The exemption attribute symbol, when referenced.</param>
    /// <returns><see langword="true" /> when an exemption covers the value type.</returns>
    private static bool HasExemption(
        INamedTypeSymbol messageType,
        INamedTypeSymbol valueType,
        INamedTypeSymbol? exemptAttribute)
    {
        if (exemptAttribute is null)
        {
            return false;
        }

        foreach (var attribute in messageType.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, exemptAttribute) ||
                attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol exempted &&
                SymbolEqualityComparer.Default.Equals(exempted, valueType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether an invocation calls a member of the declaration collector, including an extension method.
    /// </summary>
    /// <param name="called">The invoked method symbol.</param>
    /// <param name="declarations">The declaration collector symbol.</param>
    /// <returns><see langword="true" /> when the call targets the declaration collector.</returns>
    /// <remarks>
    ///     Audit declarations are extension methods on <c>IMessageDeclarations</c> in the auditing package. A reduced
    ///     extension method carries the extension class as its containing type, so the original definition has to be
    ///     read to find the receiver in the first parameter, as the module builder analyses do.
    /// </remarks>
    private static bool IsCallOnDeclarations(IMethodSymbol called, INamedTypeSymbol declarations)
    {
        if (called.ContainingType is not null &&
            SymbolEqualityComparer.Default.Equals(called.ContainingType.OriginalDefinition, declarations))
        {
            return true;
        }

        var declaredMethod = called.ReducedFrom ?? called;

        return declaredMethod is { IsExtensionMethod: true, Parameters.Length: > 0 } &&
               SymbolEqualityComparer.Default.Equals(declaredMethod.Parameters[0].Type, declarations);
    }

    /// <summary>
    ///     Determines whether an invocation hands the declaration collector to something else.
    /// </summary>
    /// <param name="invocation">The invocation in the body.</param>
    /// <param name="model">The semantic model of the body.</param>
    /// <param name="collector">The <c>Describe</c> parameter holding the collector.</param>
    /// <param name="cancellationToken">Cancels the analysis.</param>
    /// <returns><see langword="true" /> when the collector is passed as an argument.</returns>
    /// <remarks>
    ///     A body that calls a shared helper declares through it, out of this pass's sight. Treating that as covered
    ///     keeps the analyzer from failing a build over a declaration that is there.
    /// </remarks>
    private static bool PassesCollector(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        IParameterSymbol collector,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (model.GetSymbolInfo(argument.Expression, cancellationToken).Symbol is IParameterSymbol passed &&
                SymbolEqualityComparer.Default.Equals(passed, collector))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Collects every attribute class annotated as declaring the given value type.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <param name="collector">The collector holding the source attribute facts.</param>
    /// <param name="symbols">The resolved declaration symbols.</param>
    /// <param name="valueType">The metadata value type in question.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The attribute classes that declare the value type.</returns>
    /// <remarks>
    ///     Referenced assemblies are scanned as well as the analyzed one, because LiteBus's own
    ///     <c>[Audited]</c> and <c>[AuditExempt]</c> live in a package and an application's declaring attributes often
    ///     live in a shared project. The referenced-assembly walk runs here, inside the compilation-end action, while
    ///     source attributes arrive through the symbol action.
    /// </remarks>
    private static ImmutableHashSet<INamedTypeSymbol> CollectDeclaringAttributes(
        Compilation compilation,
        DeclarationCollector collector,
        DeclarationSymbols symbols,
        INamedTypeSymbol valueType,
        CancellationToken cancellationToken)
    {
        if (symbols.MessageDeclarationAnnotation is null)
        {
            return ImmutableHashSet.Create<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        }

        var builder = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var fact in collector.SourceDeclaringAttributes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SymbolEqualityComparer.Default.Equals(fact.DeclaredValue, valueType))
            {
                builder.Add(fact.AttributeClass);
            }
        }

        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol referenced)
            {
                continue;
            }

            foreach (var module in referenced.Modules)
            {
                foreach (var type in EnumerateTypes(module.GlobalNamespace, cancellationToken))
                {
                    foreach (var attribute in type.GetAttributes())
                    {
                        if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, symbols.MessageDeclarationAnnotation) ||
                            attribute.ConstructorArguments.Length == 0)
                        {
                            continue;
                        }

                        if (attribute.ConstructorArguments[0].Value is INamedTypeSymbol declared &&
                            SymbolEqualityComparer.Default.Equals(declared, valueType))
                        {
                            builder.Add(type);
                        }
                    }
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Walks a namespace and yields every type it contains, nested types included.
    /// </summary>
    /// <param name="namespaceSymbol">The namespace to walk.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>Every type reachable from the namespace.</returns>
    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(
        INamespaceSymbol namespaceSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (member)
            {
                case INamespaceSymbol nested:
                    foreach (var type in EnumerateTypes(nested, cancellationToken))
                    {
                        yield return type;
                    }

                    break;

                case INamedTypeSymbol type:
                    foreach (var nestedType in EnumerateTypeAndNested(type, cancellationToken))
                    {
                        yield return nestedType;
                    }

                    break;
            }
        }
    }

    /// <summary>
    ///     Yields a type and every type nested inside it.
    /// </summary>
    /// <param name="type">The outer type.</param>
    /// <param name="cancellationToken">The analysis cancellation token.</param>
    /// <returns>The type and its nested types.</returns>
    private static IEnumerable<INamedTypeSymbol> EnumerateTypeAndNested(
        INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        yield return type;

        foreach (var nested in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var inner in EnumerateTypeAndNested(nested, cancellationToken))
            {
                yield return inner;
            }
        }
    }
}
