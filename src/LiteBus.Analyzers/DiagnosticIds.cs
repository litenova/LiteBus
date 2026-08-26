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

    /// <summary>
    ///     Handler tag is not referenced by any publish or send filter in the compilation.
    /// </summary>
    internal const string OrphanHandlerTag = "LB1011";

    /// <summary>
    ///     Handler type name is duplicated across assemblies and may be registered twice.
    /// </summary>
    internal const string DuplicateHandlerAcrossAssemblies = "LB1012";

    /// <summary>
    ///     Type depends on transactional outbox storage without a database context in the same constructor.
    /// </summary>
    internal const string TransactionalOutboxWithoutDbContext = "LB1013";

    /// <summary>
    ///     Inbox or outbox processor is enabled without a dispatcher registration.
    /// </summary>
    internal const string ProcessorEnabledWithoutDispatcher = "LB1014";

    /// <summary>
    ///     Transactional EF storage setup omits the save-changes interceptor.
    /// </summary>
    internal const string TransactionalStorageWithoutInterceptor = "LB1015";

    /// <summary>
    ///     Type depends on transactional inbox storage without a database context in the same constructor.
    /// </summary>
    internal const string TransactionalInboxWithoutDbContext = "LB1016";

    /// <summary>
    ///     Message type declares <c>[MessageContract]</c> but lacks explicit contract registration.
    /// </summary>
    internal const string ExplicitMessageContractRegistration = "LB1017";

    /// <summary>
    ///     Command or query type states no audit position.
    /// </summary>
    internal const string MissingAuditDeclaration = "LB1018";

    /// <summary>
    ///     Gate uses the untyped gate contract for a message that produces a result.
    /// </summary>
    internal const string UntypedGateOnResultMessage = "LB1019";
}