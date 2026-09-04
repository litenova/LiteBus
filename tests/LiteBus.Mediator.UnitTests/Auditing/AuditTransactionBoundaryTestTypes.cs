using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Auditing;

/// <summary>
///     A stand-in for an application unit of work that stages writes and flushes them on commit.
/// </summary>
public sealed class FakeUnitOfWork
{
    /// <summary>
    ///     The writes staged but not yet flushed.
    /// </summary>
    private readonly List<string> _staged = [];

    /// <summary>
    ///     Gets the writes that reached storage, in the order they were staged.
    /// </summary>
    public List<string> Flushed { get; } = [];

    /// <summary>
    ///     Gets the writes made outside the transaction, which is where a record for a failure has to go.
    /// </summary>
    public List<string> OutOfBand { get; } = [];

    /// <summary>
    ///     Gets a value indicating whether the unit of work committed.
    /// </summary>
    public bool Committed { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the unit of work rolled back.
    /// </summary>
    public bool RolledBack { get; private set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the commit throws, standing in for a concurrency conflict.
    /// </summary>
    public bool FailOnCommit { get; set; }

    /// <summary>
    ///     Stages one write for the next commit.
    /// </summary>
    /// <param name="write">A description of the write.</param>
    public void Stage(string write)
    {
        _staged.Add(write);
    }

    /// <summary>
    ///     Records one write that bypassed the transaction.
    /// </summary>
    /// <param name="write">A description of the write.</param>
    public void WriteOutOfBand(string write)
    {
        OutOfBand.Add(write);
    }

    /// <summary>
    ///     Flushes every staged write.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="FailOnCommit" /> is set.</exception>
    public void Commit()
    {
        if (FailOnCommit)
        {
            _staged.Clear();
            throw new InvalidOperationException("commit failed");
        }

        Committed = true;
        Flushed.AddRange(_staged);
        _staged.Clear();
    }

    /// <summary>
    ///     Discards every staged write.
    /// </summary>
    public void Rollback()
    {
        RolledBack = true;
        _staged.Clear();
    }
}

/// <summary>
///     An audit trail that stages a successful record into the unit of work and writes every other record out of band.
/// </summary>
/// <remarks>
///     A record describing a rolled-back change cannot share the transaction being rolled back, so the branch on
///     <see cref="AuditRecord.Outcome" /> is not an optimization; it is the only way the failure record survives.
/// </remarks>
public sealed class StagingAuditTrail : IAuditTrail
{
    /// <summary>
    ///     The unit of work that flushes staged records.
    /// </summary>
    private readonly FakeUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StagingAuditTrail" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work that flushes staged records.</param>
    public StagingAuditTrail(FakeUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var write = $"audit:{record.Action}:{record.Outcome}";

        if (record.Outcome == AuditOutcome.Succeeded)
        {
            _unitOfWork.Stage(write);
        }
        else
        {
            _unitOfWork.WriteOutOfBand(write);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command whose handler stages a domain write into the unit of work.
/// </summary>
[Audited("orders.place-order")]
internal sealed class TransactionalCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the handler throws after staging.
    /// </summary>
    public bool ShouldThrow { get; set; }
}

/// <summary>
///     Declares the audit position of <see cref="TransactionalCommand" /> without an attribute, so the test covers the
///     definition path as well.
/// </summary>
internal sealed class TransactionalCommandDefinition : IAuditDefinition<TransactionalCommand>
{
    /// <inheritdoc />
    public AuditDeclaration Audit => AuditDeclaration.Audited("orders.place-order") with { Category = "orders" };
}

/// <summary>
///     Stages the domain write, then fails when the command asks for it.
/// </summary>
internal sealed class TransactionalCommandHandler : ICommandHandler<TransactionalCommand>
{
    /// <summary>
    ///     The unit of work the write is staged into.
    /// </summary>
    private readonly FakeUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransactionalCommandHandler" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work the write is staged into.</param>
    public TransactionalCommandHandler(FakeUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task HandleAsync(TransactionalCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _unitOfWork.Stage("order-placed");

        if (message.ShouldThrow)
        {
            throw new InvalidOperationException("handler failed");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     Commits the unit of work once the mediation has ended, after every LiteBus completion handler.
/// </summary>
/// <remarks>
///     The priority is what puts this after the audit writer at <see cref="HandlerPriorities.Observability" />, and the
///     outcome check is what stops a failed mediation from committing the work it half-did.
/// </remarks>
[HandlerPriority(HandlerPriorities.UnitOfWork)]
internal sealed class UnitOfWorkCommitCompletionHandler : ICommandCompletionHandler
{
    /// <summary>
    ///     The unit of work committed at the end of mediation.
    /// </summary>
    private readonly FakeUnitOfWork _unitOfWork;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UnitOfWorkCommitCompletionHandler" /> class.
    /// </summary>
    /// <param name="unitOfWork">The unit of work committed at the end of mediation.</param>
    public UnitOfWorkCommitCompletionHandler(FakeUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Outcome == MediationOutcome.Succeeded)
        {
            _unitOfWork.Commit();
        }
        else
        {
            _unitOfWork.Rollback();
        }

        return Task.CompletedTask;
    }
}
