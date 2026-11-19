using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Decides when and where an audit record is written, relative to the business commit.
/// </summary>
/// <remarks>
///     <para>
///         The timing decides what a record may truthfully claim. A record written in the business transaction claims
///         the change happened, atomically. A record delivered after commit claims the change happened, eventually and
///         reliably. A record written before the handler runs claims only that the command was attempted; it never
///         claims success.
///     </para>
///     <para>
///         Set the capture on the auditing builder and override it per message with an
///         <see cref="AuditCaptureDeclaration" />. Denial, invalid, and failure records always take the out-of-band
///         path in every mode, because there is no business change to commit with and they must survive the rollback.
///     </para>
///     <para>
///         The hierarchy is closed to the three modes LiteBus defines, so the constructor is not accessible outside
///         this assembly. Match on <see cref="WithChangeCapture" />, <see cref="AfterCommitThroughOutboxCapture" />,
///         and <see cref="BeforeAndOutcomeCapture" /> to tell them apart.
///     </para>
/// </remarks>
public abstract record AuditCapture
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCapture" /> class.
    /// </summary>
    /// <remarks>
    ///     The hierarchy is closed to the three modes LiteBus defines, so the constructor is not accessible outside
    ///     this assembly.
    /// </remarks>
    internal AuditCapture()
    {
    }

    /// <summary>
    ///     Writes the record in the business transaction or session, so it commits or rolls back with the change.
    /// </summary>
    /// <param name="Link">When the chain position is assigned.</param>
    /// <remarks>
    ///     The record claims the change happened, atomically true. This is the default when the audit store can join
    ///     the business transaction.
    /// </remarks>
    public sealed record WithChangeCapture(AuditLink Link) : AuditCapture;

    /// <summary>
    ///     Enqueues the record through the transactional outbox in the business transaction; it is delivered to the
    ///     audit store after commit, at least once, idempotent by record id.
    /// </summary>
    /// <remarks>
    ///     The record claims the change happened, eventually and reliably. Composition picks this automatically when
    ///     the store cannot join the business transaction, and lists the choice in the composition model.
    /// </remarks>
    public sealed record AfterCommitThroughOutboxCapture : AuditCapture
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="AfterCommitThroughOutboxCapture" /> class.
        /// </summary>
        private AfterCommitThroughOutboxCapture()
        {
        }

        /// <summary>
        ///     Gets the single instance of the capture mode.
        /// </summary>
        public static AfterCommitThroughOutboxCapture Instance { get; } = new();
    }

    /// <summary>
    ///     Writes an intent record in its own transaction before the handler runs; an outcome record follows when the
    ///     mediation ends.
    /// </summary>
    /// <remarks>
    ///     The intent record claims the command was attempted by the caller, and the outcome record states the result.
    ///     The intent record carries <see cref="AuditOutcome.Intent" />; a record written before commit never claims
    ///     success.
    /// </remarks>
    public sealed record BeforeAndOutcomeCapture : AuditCapture
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="BeforeAndOutcomeCapture" /> class.
        /// </summary>
        private BeforeAndOutcomeCapture()
        {
        }

        /// <summary>
        ///     Gets the single instance of the capture mode.
        /// </summary>
        public static BeforeAndOutcomeCapture Instance { get; } = new();
    }

    /// <summary>
    ///     Creates a capture that writes the record in the business transaction or session.
    /// </summary>
    /// <param name="link">
    ///     When the chain position is assigned. Defaults to <see cref="AuditLink.AfterCommit" />; composition also
    ///     accepts <see cref="AuditLink.InTransaction" /> and rejects any other pair with a configuration exception.
    /// </param>
    /// <returns>The capture mode.</returns>
    public static WithChangeCapture WithChange(AuditLink link = AuditLink.AfterCommit)
    {
        return new WithChangeCapture(link);
    }

    /// <summary>
    ///     Creates a capture that enqueues the record through the transactional outbox for delivery after commit.
    /// </summary>
    /// <returns>The capture mode, which always links <see cref="AuditLink.AtWrite" />.</returns>
    public static AfterCommitThroughOutboxCapture AfterCommitThroughOutbox()
    {
        return AfterCommitThroughOutboxCapture.Instance;
    }

    /// <summary>
    ///     Creates a capture that writes an intent record before the handler runs and an outcome record when the
    ///     mediation ends.
    /// </summary>
    /// <returns>The capture mode, which always links <see cref="AuditLink.AtWrite" />.</returns>
    public static BeforeAndOutcomeCapture BeforeAndOutcome()
    {
        return BeforeAndOutcomeCapture.Instance;
    }
}
