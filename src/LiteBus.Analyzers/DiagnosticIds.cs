namespace LiteBus.Analyzers;

/// <summary>
///     Diagnostic rule identifiers emitted by LiteBus analyzers.
/// </summary>
internal static class DiagnosticIds
{
    /// <summary>
    ///     Duplicate command handler for the same command type.
    /// </summary>
    internal const string DuplicateCommandHandler = "LB1001";

    /// <summary>
    ///     Duplicate event handler routing for the same event type.
    /// </summary>
    internal const string DuplicateEventHandler = "LB1002";

    /// <summary>
    ///     Query handler depends on side-effecting mediators or durable writers.
    /// </summary>
    internal const string QueryHandlerImpurity = "LB1003";

    /// <summary>
    ///     Command with a result type is stored through the inbox API.
    /// </summary>
    internal const string CommandWithResultScheduledToInbox = "LB1004";

    /// <summary>
    ///     Open generic handler type has an unsupported shape.
    /// </summary>
    internal const string UnsupportedOpenGenericHandler = "LB1005";

    /// <summary>
    ///     Handlers for the same message and pipeline stage share a priority value.
    /// </summary>
    internal const string HandlerPriorityConflict = "LB1006";

    /// <summary>
    ///     Message type lacks a durable contract registration.
    /// </summary>
    internal const string MissingMessageContractRegistration = "LB1007";
}
