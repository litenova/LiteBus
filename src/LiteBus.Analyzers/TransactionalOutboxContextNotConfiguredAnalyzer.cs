using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports transactional outboxes injected for contexts no storage call in the compilation configures.
/// </summary>
/// <remarks>
///     <para>
///         Resolving <c>ITransactionalOutbox&lt;TContext&gt;</c> for a context that was never passed to
///         <c>UseDbContext&lt;TContext&gt;()</c> fails on the first enqueue. Matching injected contexts against the
///         storage calls at compilation end moves that failure from the first production command to the build.
///     </para>
///     <para>
///         The rule reports only when the compilation holds at least one <c>UseDbContext&lt;TContext&gt;()</c> call
///         on an inbox or outbox storage builder, because the configuring call may otherwise live in another assembly.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TransactionalOutboxContextNotConfiguredAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.TransactionalOutboxContextNotConfigured];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(AnalyzeCompilationStart);
    }

    /// <summary>
    ///     Gathers configured contexts and injected transactional outboxes for one compilation.
    /// </summary>
    /// <param name="context">The compilation start analysis context.</param>
    private static void AnalyzeCompilationStart(CompilationStartAnalysisContext context)
    {
        if (context.Compilation.GetTypeByMetadataName("LiteBus.Outbox.Abstractions.ITransactionalOutbox`1") is null)
        {
            return;
        }

        var configuredContexts = new ConcurrentBag<ITypeSymbol>();
        var injections = new ConcurrentBag<(string InjectingTypeName, ITypeSymbol Context, Location Location)>();

        context.RegisterSymbolAction(
            symbolContext => AnalyzeNamedType(symbolContext, injections),
            SymbolKind.NamedType);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeInvocation(nodeContext, configuredContexts),
            SyntaxKind.InvocationExpression);

        context.RegisterCompilationEndAction(
            endContext => AnalyzeCompilationEnd(endContext, configuredContexts, injections));
    }

    /// <summary>
    ///     Records the transactional outbox contexts injected through one type's public constructors.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="injections">The injected contexts, one entry per constructor parameter.</param>
    /// <remarks>
    ///     Only public constructors are inspected, matching the transactional-constructor rules: a private test-seam
    ///     constructor is never called by the container, so its parameters are not injected dependencies.
    /// </remarks>
    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        ConcurrentBag<(string InjectingTypeName, ITypeSymbol Context, Location Location)> injections)
    {
        if (context.Symbol is not INamedTypeSymbol typeSymbol ||
            !SymbolEqualityComparer.Default.Equals(typeSymbol.ContainingAssembly, context.Compilation.Assembly))
        {
            return;
        }

        var transactionalOutbox = context.Compilation.GetTypeByMetadataName("LiteBus.Outbox.Abstractions.ITransactionalOutbox`1");

        if (transactionalOutbox is null)
        {
            return;
        }

        foreach (var constructor in typeSymbol.InstanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public))
        {
            foreach (var parameter in constructor.Parameters)
            {
                RecordInjection(context, typeSymbol, parameter, transactionalOutbox, injections);
            }
        }
    }

    /// <summary>
    ///     Records one constructor parameter when it is a transactional outbox for a context.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="typeSymbol">The type declaring the constructor.</param>
    /// <param name="parameter">The constructor parameter symbol.</param>
    /// <param name="transactionalOutbox">The open generic transactional outbox symbol.</param>
    /// <param name="injections">The injected contexts, one entry per constructor parameter.</param>
    private static void RecordInjection(
        SymbolAnalysisContext context,
        INamedTypeSymbol typeSymbol,
        IParameterSymbol parameter,
        INamedTypeSymbol transactionalOutbox,
        ConcurrentBag<(string InjectingTypeName, ITypeSymbol Context, Location Location)> injections)
    {
        if (parameter.Type is not INamedTypeSymbol namedParameter ||
            !SymbolEqualityComparer.Default.Equals(namedParameter.OriginalDefinition, transactionalOutbox) ||
            namedParameter.TypeArguments.Length != 1)
        {
            return;
        }

        var location = LiteBusSymbols.GetDiagnosticLocation(
            context.Compilation,
            parameter.Locations.FirstOrDefault() ?? Location.None);

        injections.Add((typeSymbol.Name, namedParameter.TypeArguments[0], location));
    }

    /// <summary>
    ///     Records the context configured by one <c>UseDbContext</c> invocation, when it configures storage.
    /// </summary>
    /// <param name="context">The syntax node analysis context.</param>
    /// <param name="configuredContexts">The contexts passed to storage builder <c>UseDbContext</c> calls.</param>
    /// <remarks>
    ///     The invoked name is checked first, so invocations that cannot match cost one string comparison.
    /// </remarks>
    private static void AnalyzeInvocation(
        SyntaxNodeAnalysisContext context,
        ConcurrentBag<ITypeSymbol> configuredContexts)
    {
        if (context.Node is not InvocationExpressionSyntax invocation ||
            !IsUseDbContextName(invocation.Expression))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol is not IMethodSymbol method ||
            method.TypeArguments.Length != 1 ||
            !IsStorageBuilderUseDbContext(method))
        {
            return;
        }

        configuredContexts.Add(method.TypeArguments[0]);
    }

    /// <summary>
    ///     Reports injected transactional outbox contexts that no storage call configures.
    /// </summary>
    /// <param name="context">The compilation analysis context.</param>
    /// <param name="configuredContexts">The contexts passed to storage builder <c>UseDbContext</c> calls.</param>
    /// <param name="injections">The injected contexts, one entry per constructor parameter.</param>
    private static void AnalyzeCompilationEnd(
        CompilationAnalysisContext context,
        ConcurrentBag<ITypeSymbol> configuredContexts,
        ConcurrentBag<(string InjectingTypeName, ITypeSymbol Context, Location Location)> injections)
    {
        if (configuredContexts.IsEmpty || injections.IsEmpty)
        {
            return;
        }

        var configured = new HashSet<ITypeSymbol>(configuredContexts, SymbolEqualityComparer.Default);
        var order = CompilationEndOrdering.GetSyntaxTreeOrder(context.Compilation);

        var unconfigured = injections
            .Where(injection => !configured.Contains(injection.Context))
            .OrderBy(injection => CompilationEndOrdering.GetOrderKey(injection.Location, order)?.TreeIndex ?? int.MaxValue)
            .ThenBy(injection => CompilationEndOrdering.GetOrderKey(injection.Location, order)?.SpanStart ?? int.MaxValue)
            .ThenBy(injection => injection.InjectingTypeName, StringComparer.Ordinal)
            .ThenBy(injection => injection.Context.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), StringComparer.Ordinal)
            .ToList();

        foreach (var injection in unconfigured)
        {
            var contextDisplay = injection.Context.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.TransactionalOutboxContextNotConfigured,
                injection.Location,
                injection.InjectingTypeName,
                contextDisplay));
        }
    }

    /// <summary>
    ///     Determines whether the invoked expression names <c>UseDbContext</c> without binding.
    /// </summary>
    /// <param name="expression">The invoked expression.</param>
    /// <returns><see langword="true" /> when the invoked name is <c>UseDbContext</c>; otherwise, <see langword="false" />.</returns>
    private static bool IsUseDbContextName(ExpressionSyntax expression)
    {
        var name = expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };

        return string.Equals(name, "UseDbContext", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Determines whether the method is <c>UseDbContext</c> on an inbox or outbox storage builder.
    /// </summary>
    /// <param name="method">The invoked method symbol.</param>
    /// <returns><see langword="true" /> when the method configures a storage builder context; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Only the Entity Framework Core inbox and outbox storage builders count. A same-named method on any other
    ///     type configures something else.
    /// </remarks>
    private static bool IsStorageBuilderUseDbContext(IMethodSymbol method)
    {
        if (!string.Equals(method.Name, "UseDbContext", StringComparison.Ordinal) ||
            method.ContainingType is null)
        {
            return false;
        }

        var namespaceName = method.ContainingType.ContainingNamespace?.ToDisplayString();

        return (string.Equals(namespaceName, "LiteBus.Outbox.Storage.EntityFrameworkCore", StringComparison.Ordinal) &&
                string.Equals(method.ContainingType.Name, "EfCoreOutboxStorageModuleBuilder", StringComparison.Ordinal)) ||
            (string.Equals(namespaceName, "LiteBus.Inbox.Storage.EntityFrameworkCore", StringComparison.Ordinal) &&
                string.Equals(method.ContainingType.Name, "EfCoreInboxStorageModuleBuilder", StringComparison.Ordinal));
    }
}
