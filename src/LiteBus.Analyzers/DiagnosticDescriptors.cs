using Microsoft.CodeAnalysis;

namespace LiteBus.Analyzers;

/// <summary>
///     Shared diagnostic descriptors for LiteBus analyzer rules.
/// </summary>
public static class DiagnosticDescriptors
{
    /// <summary>
    ///     Two command handlers are registered for the same command type.
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateCommandHandler = new(
        DiagnosticIds.DuplicateCommandHandler,
        "Duplicate command handler",
        "Command type '{0}' has more than one command handler ('{1}' and '{2}'). Each command type must have exactly one handler.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A query handler depends on a side-effecting mediator or durable writer.
    /// </summary>
    internal static readonly DiagnosticDescriptor QueryHandlerImpurity = new(
        DiagnosticIds.QueryHandlerImpurity,
        "Query handler impurity",
        "Query handler '{0}' depends on '{1}'. Query handlers should be side-effect free and must not use command, event, inbox, or outbox APIs.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     A command with a result type is stored through the inbox API.
    /// </summary>
    internal static readonly DiagnosticDescriptor CommandWithResultScheduledToInbox = new(
        DiagnosticIds.CommandWithResultScheduledToInbox,
        "Command with result scheduled to inbox",
        "Type '{0}' implements ICommand<{1}> and cannot be stored through IInbox.AcceptAsync or AcceptBatchAsync. Use a void command for inbox storage or send the command immediately through ICommandMediator.",
        "LiteBus.Inbox",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     An open generic handler type has an unsupported generic arity.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnsupportedOpenGenericHandler = new(
        DiagnosticIds.UnsupportedOpenGenericHandler,
        "Unsupported open generic handler",
        "Open generic handler '{0}' exposes {1} type parameters. LiteBus open generic handlers must expose exactly one type parameter that matches the handled message type.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     A handled message type lacks a durable contract registration.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingMessageContractRegistration = new(
        DiagnosticIds.MissingMessageContractRegistration,
        "Missing message contract registration",
        "Message type '{0}' is handled by '{1}' but has no durable contract registration. Apply [MessageContract(\"name\", version)] or call Contracts.Register<{2}>(...) during module configuration.",
        "LiteBus.Contracts",
        DiagnosticSeverity.Warning,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A command type has no main command handler in the compilation.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingCommandHandler = new(
        DiagnosticIds.MissingCommandHandler,
        "Missing command handler",
        "Command type '{0}' has no command handler. Register ICommandHandler<{0}> or a handler for a base command type that covers it.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A query type has no main query handler in the compilation.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingQueryHandler = new(
        DiagnosticIds.MissingQueryHandler,
        "Missing query handler",
        "Query type '{0}' has no query handler. Register IQueryHandler<{0}, TResult> or a handler for a base query type that covers it.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     Two query handlers are registered for the same query type.
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateQueryHandler = new(
        DiagnosticIds.DuplicateQueryHandler,
        "Duplicate query handler",
        "Query type '{0}' has more than one query handler ('{1}' and '{2}'). Each query type must have exactly one handler.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A handler tag is not referenced by any command or event mediation filter in the compilation.
    /// </summary>
    internal static readonly DiagnosticDescriptor OrphanHandlerTag = new(
        DiagnosticIds.OrphanHandlerTag,
        "Orphan handler tag",
        "Handler '{0}' is tagged with '{1}', but no command, query, or event mediation filter references that tag in this compilation",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     The same handler type name appears in multiple assemblies and may be registered twice.
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateHandlerAcrossAssemblies = new(
        DiagnosticIds.DuplicateHandlerAcrossAssemblies,
        "Duplicate handler across assemblies",
        "Handler name '{0}' is declared in assemblies '{1}' and '{2}'. RegisterFromAssembly may register both handlers for the same message type.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A type depends on transactional outbox storage without a database context in the same constructor.
    /// </summary>
    internal static readonly DiagnosticDescriptor TransactionalOutboxWithoutDbContext = new(
        DiagnosticIds.TransactionalOutboxWithoutDbContext,
        "Transactional outbox without DbContext",
        "Type '{0}' injects ITransactionalOutboxStore but does not inject a DbContext in the same constructor. Transactional outbox requires an active EF Core unit of work.",
        "LiteBus.Outbox",
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     An inbox or outbox processor is enabled without a dispatcher registration.
    /// </summary>
    internal static readonly DiagnosticDescriptor ProcessorEnabledWithoutDispatcher = new(
        DiagnosticIds.ProcessorEnabledWithoutDispatcher,
        "Processor enabled without dispatcher",
        "{0} enables the background processor but does not register a dispatcher in the same configuration scope. Call {2}, a broker-specific Use*Dispatch extension, or RegisterDispatcher before Enable{1}Processor.",
        "LiteBus.Configuration",
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    ///     Transactional EF storage setup omits the save-changes interceptor.
    /// </summary>
    internal static readonly DiagnosticDescriptor TransactionalStorageWithoutInterceptor = new(
        DiagnosticIds.TransactionalStorageWithoutInterceptor,
        "Transactional storage without interceptor",
        "{0} calls EnforceTransactionalSetup() without EnableSaveChangesInterceptor() in the same configuration scope. Transactional inbox and outbox require the EF Core save-changes interceptor.",
        "LiteBus.Configuration",
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     A type depends on transactional inbox storage without a database context in the same constructor.
    /// </summary>
    internal static readonly DiagnosticDescriptor TransactionalInboxWithoutDbContext = new(
        DiagnosticIds.TransactionalInboxWithoutDbContext,
        "Transactional inbox without DbContext",
        "Type '{0}' injects ITransactionalInboxStore but does not inject a DbContext in the same constructor. Transactional inbox requires an active EF Core unit of work.",
        "LiteBus.Inbox",
        DiagnosticSeverity.Warning,
        true);

    /// <summary>
    ///     A message type declares <c>[MessageContract]</c> but lacks explicit contract registration.
    /// </summary>
    internal static readonly DiagnosticDescriptor ExplicitMessageContractRegistration = new(
        DiagnosticIds.ExplicitMessageContractRegistration,
        "Explicit message contract registration recommended",
        "Message type '{0}' declares [MessageContract] but has no explicit Contracts.Register<{0}> or RegisterFromAssembly configuration. Runtime on-demand resolution still works; register explicitly for predictable contract discovery.",
        "LiteBus.Contracts",
        DiagnosticSeverity.Warning,
        true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A command or query type states no audit position.
    /// </summary>
    /// <remarks>
    ///     Disabled by default. Enable with <c>dotnet_diagnostic.LB1018.severity = warning</c> once the codebase has
    ///     declared its position, since turning it on silently would break every existing compilation.
    /// </remarks>
    internal static readonly DiagnosticDescriptor MissingAuditDeclaration = new(
        DiagnosticIds.MissingAuditDeclaration,
        "Message states no audit position",
        "Message type '{0}' declares neither [Audited] nor [AuditExempt] and has no IAuditDefinition. State the position explicitly so that an unaudited message is a recorded decision rather than an oversight.",
        "LiteBus.Auditing",
        DiagnosticSeverity.Warning,
        false,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>
    ///     A shortcut uses the untyped shortcut contract for a message that produces a result.
    /// </summary>
    internal static readonly DiagnosticDescriptor UntypedShortcutOnResultMessage = new(
        DiagnosticIds.UntypedShortcutOnResultMessage,
        "Untyped shortcut on a message that produces a result",
        "Shortcut '{0}' implements the untyped shortcut contract for '{1}', which produces '{2}'. The untyped answer cannot carry a result, so answering fails at runtime with LiteBusConfigurationException. Implement {3}<{1}, {2}> instead.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
        true);
}