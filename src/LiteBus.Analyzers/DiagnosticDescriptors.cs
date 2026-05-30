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
    ///     Two event handlers for the same event type have overlapping routing tags.
    /// </summary>
    internal static readonly DiagnosticDescriptor DuplicateEventHandler = new(
        DiagnosticIds.DuplicateEventHandler,
        "Duplicate event handler routing",
        "Event type '{0}' has multiple event handlers with overlapping routing ('{1}' and '{2}'). Use distinct handler tags or merge the handlers.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
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
    ///     Two handlers for the same message and pipeline stage share a priority value.
    /// </summary>
    internal static readonly DiagnosticDescriptor HandlerPriorityConflict = new(
        DiagnosticIds.HandlerPriorityConflict,
        "Handler priority conflict",
        "Message type '{0}' has multiple {1} handlers with priority {2} ('{3}' and '{4}'). Assign distinct priorities when execution order matters.",
        "LiteBus.Handlers",
        DiagnosticSeverity.Warning,
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
}
