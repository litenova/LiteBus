using System.Collections.Immutable;
using System.Linq;
using LiteBus.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace LiteBus.Analyzers;

/// <summary>
///     Reports command handlers that inject the non-transactional outbox.
/// </summary>
/// <remarks>
///     <para>
///         Inside a unit of work, <c>IOutbox.EnqueueAsync</c> writes and commits the envelope at once, so a later
///         rollback of the domain write still leaves a published event. <c>ITransactionalOutbox&lt;TContext&gt;</c>
///         stages the envelope in the same <c>DbContext</c> transaction instead, so the outbox row commits with the
///         domain write.
///     </para>
///     <para>
///         The rule reports only when the compilation can resolve the transactional outbox, because a compilation
///         without it has no transactional injection to switch to.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonTransactionalOutboxInCommandHandlerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The open generic command handler interfaces covered by this rule.
    /// </summary>
    private static readonly string[] CommandHandlerInterfaceMetadataNames =
    [
        "LiteBus.Commands.Abstractions.ICommandHandler`1",
        "LiteBus.Commands.Abstractions.ICommandHandler`2"
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    /// <summary>
    ///     Reports public constructor parameters typed as the non-transactional outbox.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <remarks>
    ///     Only public constructors are inspected, matching the transactional-constructor rules: a private test-seam
    ///     constructor is never called by the container, so its parameters are not injected dependencies.
    /// </remarks>
    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol handlerType ||
            handlerType.TypeKind != TypeKind.Class ||
            !SymbolEqualityComparer.Default.Equals(handlerType.ContainingAssembly, context.Compilation.Assembly))
        {
            return;
        }

        if (!IsCommandHandler(handlerType, context.Compilation))
        {
            return;
        }

        var outbox = context.Compilation.GetTypeByMetadataName("LiteBus.Outbox.Abstractions.IOutbox");

        if (outbox is null)
        {
            return;
        }

        if (!TransactionalOutboxResolves(context.Compilation))
        {
            return;
        }

        foreach (var constructor in handlerType.InstanceConstructors.Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public))
        {
            foreach (var parameter in constructor.Parameters)
            {
                ReportIfNonTransactionalOutbox(context, handlerType, parameter, outbox);
            }
        }
    }

    /// <summary>
    ///     Determines whether the type implements a command handler interface.
    /// </summary>
    /// <param name="type">The candidate handler type symbol.</param>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns><see langword="true" /> when the type implements a command handler interface; otherwise, <see langword="false" />.</returns>
    /// <remarks>
    ///     Only <c>ICommandHandler&lt;TCommand&gt;</c> and <c>ICommandHandler&lt;TCommand, TCommandResult&gt;</c>
    ///     count. Messaging-level handlers whose message type is a command stay out of scope.
    /// </remarks>
    private static bool IsCommandHandler(INamedTypeSymbol type, Compilation compilation)
    {
        foreach (var metadataName in CommandHandlerInterfaceMetadataNames)
        {
            if (LiteBusSymbols.ImplementsGenericInterface(type, compilation, metadataName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the compilation can see an outbox storage package that offers a transactional outbox.
    /// </summary>
    /// <param name="compilation">The compilation being analyzed.</param>
    /// <returns>
    ///     <see langword="true" /> when the EF Core, PostgreSQL or Marten outbox storage registration type resolves;
    ///     otherwise, <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     <c>ITransactionalOutbox&lt;TContext&gt;</c> itself lives in the outbox abstractions, which every outbox
    ///     application references, so its presence says nothing. Only a transactional storage package makes the
    ///     non-transactional outbox the wrong choice inside a command handler.
    /// </remarks>
    private static bool TransactionalOutboxResolves(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("LiteBus.Outbox.Storage.EntityFrameworkCore.OutboxModuleBuilderEfCoreExtensions") is not null
            || compilation.GetTypeByMetadataName("LiteBus.Outbox.Storage.PostgreSql.OutboxModuleBuilderPostgreSqlExtensions") is not null
            || compilation.GetTypeByMetadataName("LiteBus.Outbox.Storage.Marten.PostgreSqlOutboxModuleBuilderMartenExtensions") is not null;
    }

    /// <summary>
    ///     Reports a diagnostic when the parameter type is the non-transactional outbox.
    /// </summary>
    /// <param name="context">The symbol analysis context.</param>
    /// <param name="handlerType">The command handler type symbol.</param>
    /// <param name="parameter">The constructor parameter symbol.</param>
    /// <param name="outbox">The non-transactional outbox interface symbol.</param>
    private static void ReportIfNonTransactionalOutbox(
        SymbolAnalysisContext context,
        INamedTypeSymbol handlerType,
        IParameterSymbol parameter,
        INamedTypeSymbol outbox)
    {
        if (!IsOutboxType(parameter.Type, outbox))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.NonTransactionalOutboxInCommandHandler,
            LiteBusSymbols.GetDiagnosticLocation(
                context.Compilation,
                parameter.Locations.FirstOrDefault() ?? Location.None),
            handlerType.Name));
    }

    /// <summary>
    ///     Determines whether the parameter type is the outbox type or implements it.
    /// </summary>
    /// <param name="parameterType">The parameter type symbol.</param>
    /// <param name="outbox">The non-transactional outbox interface symbol.</param>
    /// <returns><see langword="true" /> when the parameter type is the outbox; otherwise, <see langword="false" />.</returns>
    private static bool IsOutboxType(ITypeSymbol parameterType, INamedTypeSymbol outbox)
    {
        if (SymbolEqualityComparer.Default.Equals(parameterType.OriginalDefinition, outbox))
        {
            return true;
        }

        foreach (var candidate in parameterType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, outbox))
            {
                return true;
            }
        }

        return false;
    }
}
