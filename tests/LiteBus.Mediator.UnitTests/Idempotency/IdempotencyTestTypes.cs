using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Counts how many times a handler actually ran, which is what idempotency is asserted on.
/// </summary>
public sealed class ApplicationCounter
{
    /// <summary>
    ///     Gets or sets the number of handler invocations.
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
///     The result <see cref="RepeatablePaymentCommand" /> and <see cref="SettlePaymentCommand" /> produce.
/// </summary>
/// <param name="Reference">The payment reference.</param>
public sealed record PaymentReceipt(string Reference);

/// <summary>
///     A command declaring an idempotency key and producing no result.
/// </summary>
internal sealed class ApplyPaymentCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets a value indicating whether the handler throws, so the claim has to be released.
    /// </summary>
    public bool ShouldThrow { get; set; }
}

/// <summary>
///     Declares how a repeat of <see cref="ApplyPaymentCommand" /> is recognised.
/// </summary>
internal sealed class ApplyPaymentCommandDefinition : IIdempotencyDefinition<ApplyPaymentCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ApplyPaymentCommand>(command => command.PaymentId);
}

/// <summary>
///     Applies the payment, or fails when the command asks for it.
/// </summary>
internal sealed class ApplyPaymentCommandHandler : ICommandHandler<ApplyPaymentCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ApplyPaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public ApplyPaymentCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(ApplyPaymentCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;

        if (message.ShouldThrow)
        {
            throw new InvalidOperationException("the ledger rejected the payment");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
///     A second command declaring the same raw key value, to prove keys are scoped per message type.
/// </summary>
internal sealed class ReservePaymentCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>
///     Declares how a repeat of <see cref="ReservePaymentCommand" /> is recognised.
/// </summary>
internal sealed class ReservePaymentCommandDefinition : IIdempotencyDefinition<ReservePaymentCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<ReservePaymentCommand>(command => command.PaymentId);
}

/// <summary>
///     Reserves the payment.
/// </summary>
internal sealed class ReservePaymentCommandHandler : ICommandHandler<ReservePaymentCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ReservePaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public ReservePaymentCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(ReservePaymentCommand message, CancellationToken cancellationToken = default)
    {
        _applications.Count++;
        return Task.CompletedTask;
    }
}

/// <summary>
///     A command producing a result whose declaration asks for the result to be replayed to a repeat.
/// </summary>
internal sealed class RepeatablePaymentCommand : ICommand<PaymentReceipt>
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>
///     Declares a replayable repeat for <see cref="RepeatablePaymentCommand" />.
/// </summary>
internal sealed class RepeatablePaymentCommandDefinition : IIdempotencyDefinition<RepeatablePaymentCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<RepeatablePaymentCommand>(command => command.PaymentId)
            with { ReplayResult = true };
}

/// <summary>
///     Produces a receipt for the payment.
/// </summary>
internal sealed class RepeatablePaymentCommandHandler : ICommandHandler<RepeatablePaymentCommand, PaymentReceipt>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RepeatablePaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public RepeatablePaymentCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task<PaymentReceipt> HandleAsync(
        RepeatablePaymentCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;

        return Task.FromResult(new PaymentReceipt($"receipt-{message.PaymentId}"));
    }
}

/// <summary>
///     A command producing a result whose declaration does not ask for a replay, so a repeat has no answer to give.
/// </summary>
internal sealed class SettlePaymentCommand : ICommand<PaymentReceipt>
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>
///     Declares a key for <see cref="SettlePaymentCommand" /> without asking for a replay.
/// </summary>
internal sealed class SettlePaymentCommandDefinition : IIdempotencyDefinition<SettlePaymentCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<SettlePaymentCommand>(command => command.PaymentId);
}

/// <summary>
///     Settles the payment.
/// </summary>
internal sealed class SettlePaymentCommandHandler : ICommandHandler<SettlePaymentCommand, PaymentReceipt>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SettlePaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public SettlePaymentCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task<PaymentReceipt> HandleAsync(
        SettlePaymentCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _applications.Count++;

        return Task.FromResult(new PaymentReceipt($"receipt-{message.PaymentId}"));
    }
}

/// <summary>
///     A command that declares no idempotency, so the shipped handlers leave it alone.
/// </summary>
internal sealed class UndeclaredPaymentCommand : ICommand;

/// <summary>
///     Handles <see cref="UndeclaredPaymentCommand" />.
/// </summary>
internal sealed class UndeclaredPaymentCommandHandler : ICommandHandler<UndeclaredPaymentCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ApplicationCounter _applications;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UndeclaredPaymentCommandHandler" /> class.
    /// </summary>
    /// <param name="applications">The counter shared with the test.</param>
    public UndeclaredPaymentCommandHandler(ApplicationCounter applications)
    {
        _applications = applications;
    }

    /// <inheritdoc />
    public Task HandleAsync(UndeclaredPaymentCommand message, CancellationToken cancellationToken = default)
    {
        _applications.Count++;
        return Task.CompletedTask;
    }
}
