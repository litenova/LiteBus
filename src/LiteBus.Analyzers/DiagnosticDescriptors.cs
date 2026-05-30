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
        isEnabledByDefault: true);

    /// <summary>
    ///     A query handler depends on a side-effecting mediator or durable writer.
    /// </summary>
    internal static readonly DiagnosticDescriptor QueryHandlerImpurity = new(
        DiagnosticIds.QueryHandlerImpurity,
        "Query handler impurity",
        "Query handler '{0}' depends on '{1}'. Query handlers should be side-effect free and must not use command, event, inbox, or outbox APIs.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    ///     A command with a result type is stored through the inbox API.
    /// </summary>
    internal static readonly DiagnosticDescriptor CommandWithResultScheduledToInbox = new(
        DiagnosticIds.CommandWithResultScheduledToInbox,
        "Command with result scheduled to inbox",
        "Type '{0}' implements ICommand<{1}> and cannot be stored through IInbox.AddAsync. Use a void command for inbox storage or send the command immediately through ICommandMediator.",
        "LiteBus.Inbox",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     An open generic handler type has an unsupported generic arity.
    /// </summary>
    internal static readonly DiagnosticDescriptor UnsupportedOpenGenericHandler = new(
        DiagnosticIds.UnsupportedOpenGenericHandler,
        "Unsupported open generic handler",
        "Open generic handler '{0}' exposes {1} type parameters. LiteBus open generic handlers must expose exactly one type parameter that matches the handled message type.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     A handled message type lacks a durable contract registration.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingMessageContractRegistration = new(
        DiagnosticIds.MissingMessageContractRegistration,
        "Missing message contract registration",
        "Message type '{0}' is handled by '{1}' but has no durable contract registration. Apply [MessageContract(\"name\", version)] or call Contracts.Register<{0}>(...) during module configuration.",
        "LiteBus.Contracts",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    ///     A command type has no main command handler in the compilation.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingCommandHandler = new(
        DiagnosticIds.MissingCommandHandler,
        "Missing command handler",
        "Command type '{0}' has no command handler. Register ICommandHandler<{0}> or a handler for a base command type that covers it.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     A query type has no main query handler in the compilation.
    /// </summary>
    internal static readonly DiagnosticDescriptor MissingQueryHandler = new(
        DiagnosticIds.MissingQueryHandler,
        "Missing query handler",
        "Query type '{0}' has no query handler. Register IQueryHandler<{0}, TResult> or a handler for a base query type that covers it.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    ///     Two query handlers are registered for the same query type.
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateQueryHandler = new(
        DiagnosticIds.DuplicateQueryHandler,
        "Duplicate query handler",
        "Query type '{0}' has more than one query handler ('{1}' and '{2}'). Each query type must have exactly one handler.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
