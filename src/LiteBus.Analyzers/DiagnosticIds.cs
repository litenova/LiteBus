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
    ///     Message type lacks a durable contract registration.
    /// </summary>
    internal const string MissingMessageContractRegistration = "LB1007";

    /// <summary>
    ///     Command type has no main command handler in the compilation.
    /// </summary>
    internal const string MissingCommandHandler = "LB1008";

    /// <summary>
    ///     Query type has no main query handler in the compilation.
    /// </summary>
    internal const string MissingQueryHandler = "LB1009";

    /// <summary>
    ///     Duplicate query handler for the same query type.
    /// </summary>
    internal const string DuplicateQueryHandler = "LB1010";
}
