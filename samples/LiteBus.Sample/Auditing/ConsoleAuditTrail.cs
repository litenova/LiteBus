using LiteBus.Messaging.Abstractions;

namespace LiteBus.Sample.Auditing;

/// <summary>
///     Writes audit records to the application log.
/// </summary>
/// <remarks>
///     A real trail writes to storage that the application role can only insert into, chains each entry to the last for
///     tamper evidence, and enforces a retention rule per category. Those are storage concerns, which is why LiteBus
///     hands the record over rather than persisting it.
/// </remarks>
public sealed class ConsoleAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Writes one audit record at information level.
    /// </summary>
    private static readonly Action<ILogger, string, AuditOutcome, string?, string?, string?, Exception?> AuditRecorded =
        LoggerMessage.Define<string, AuditOutcome, string?, string?, string?>(
            LogLevel.Information,
            new EventId(1, nameof(AuditRecorded)),
            "Audit {Action} {Outcome} target={TargetKind}:{TargetId} failure={FailureCode}");

    /// <summary>
    ///     The logger the records are written to.
    /// </summary>
    private readonly ILogger<ConsoleAuditTrail> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ConsoleAuditTrail" /> class.
    /// </summary>
    /// <param name="logger">The logger the records are written to.</param>
    public ConsoleAuditTrail(ILogger<ConsoleAuditTrail> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        AuditRecorded(
            _logger,
            record.Action,
            record.Outcome,
            record.TargetKind,
            record.TargetId,
            record.FailureCode,
            null);

        return Task.CompletedTask;
    }
}
