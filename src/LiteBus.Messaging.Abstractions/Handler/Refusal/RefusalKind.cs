namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     The coarse kinds of refusal, used for default mappings on every transport.
/// </summary>
/// <remarks>
///     <para>
///         A code already implies a kind today, but only the HTTP package knows the mapping. This type moves that
///         knowledge into the mediation contracts, so HTTP, gRPC, dead-lettering, and command-line tools share one
///         classification instead of each deriving it from code prefixes.
///     </para>
///     <para>
///         The set is closed to the five kinds declared here. A fault is never a kind: a configuration or dependency
///         failure ends the mediation as <see cref="MediationOutcome.Failed" /> with a fault code, while a refusal is
///         an expected end the caller can act on.
///     </para>
/// </remarks>
public abstract record RefusalKind
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RefusalKind" /> class.
    /// </summary>
    /// <remarks>
    ///     Internal so only the nested kinds declared here can derive from this record.
    /// </remarks>
    internal RefusalKind()
    {
    }

    /// <summary>
    ///     Gets a refusal the pipeline denied.
    /// </summary>
    public static DeniedKind Denied { get; } = new();

    /// <summary>
    ///     Gets input the validators rejected.
    /// </summary>
    public static InvalidKind Invalid { get; } = new();

    /// <summary>
    ///     Gets a state conflict such as a concurrency or idempotency collision.
    /// </summary>
    public static ConflictKind Conflict { get; } = new();

    /// <summary>
    ///     Gets a missing resource.
    /// </summary>
    public static NotFoundKind NotFound { get; } = new();

    /// <summary>
    ///     Gets a refusal caused by a dependency that is temporarily unavailable.
    /// </summary>
    public static UnavailableKind Unavailable { get; } = new();

    /// <summary>
    ///     The kind of a refusal produced when a guard or policy denied the message.
    /// </summary>
    public sealed record DeniedKind : RefusalKind;

    /// <summary>
    ///     The kind of a refusal produced when validators rejected the message.
    /// </summary>
    public sealed record InvalidKind : RefusalKind;

    /// <summary>
    ///     The kind of a refusal produced by a state conflict, such as a concurrency or idempotency collision.
    /// </summary>
    public sealed record ConflictKind : RefusalKind;

    /// <summary>
    ///     The kind of a refusal produced when the message targets a missing resource.
    /// </summary>
    public sealed record NotFoundKind : RefusalKind;

    /// <summary>
    ///     The kind of a refusal produced when a dependency the mediation needs is temporarily unavailable.
    /// </summary>
    public sealed record UnavailableKind : RefusalKind;
}
