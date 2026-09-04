using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     A command whose amount and reference the validators inspect.
/// </summary>
internal sealed class TransferCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the amount to transfer, which must be positive to be well-formed.
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    ///     Gets or sets the reference, which must be supplied to be well-formed.
    /// </summary>
    public string? Reference { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether the caller is permitted to transfer.
    /// </summary>
    public bool IsPermitted { get; set; } = true;

    /// <summary>
    ///     Gets or sets a value indicating whether the main handler ran.
    /// </summary>
    public bool HandlerRan { get; set; }

    /// <summary>
    ///     Gets the names of the stages that ran, in the order they ran.
    /// </summary>
    public List<string> StagesRun { get; } = [];
}

/// <summary>
///     Handles the transfer command.
/// </summary>
internal sealed class TransferCommandHandler : ICommandHandler<TransferCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(TransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.HandlerRan = true;
        message.StagesRun.Add("handler");

        return Task.CompletedTask;
    }
}

/// <summary>
///     Reports a transfer whose amount is not positive.
/// </summary>
internal sealed class TransferAmountValidator : ICommandValidator<TransferCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(TransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.StagesRun.Add("amount-validator");

        return Task.FromResult(message.Amount <= 0
            ? Validity.Invalid("the amount must be positive", nameof(TransferCommand.Amount), "AMOUNT")
            : Validity.Valid);
    }
}

/// <summary>
///     Reports a transfer with no reference.
/// </summary>
/// <remarks>
///     Registered alongside <see cref="TransferAmountValidator" /> so a command failing both proves the stage collects
///     from every validator rather than stopping at the first.
/// </remarks>
internal sealed class TransferReferenceValidator : ICommandValidator<TransferCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(TransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.StagesRun.Add("reference-validator");

        return Task.FromResult(string.IsNullOrWhiteSpace(message.Reference)
            ? Validity.Invalid("the reference must be supplied", nameof(TransferCommand.Reference), "REFERENCE")
            : Validity.Valid);
    }
}

/// <summary>
///     Refuses a transfer the caller is not permitted to make.
/// </summary>
internal sealed class TransferPermissionGuard : ICommandGuard<TransferCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.StagesRun.Add("guard");

        return Task.FromResult(message.IsPermitted
            ? Verdict.Allow
            : Verdict.Deny("the caller may not transfer", "NOT_PERMITTED"));
    }
}

/// <summary>
///     Answers every transfer, standing in for a cache or an idempotency check.
/// </summary>
/// <remarks>
///     Carries the lowest possible priority and is registered for the concrete command, which under priority ordering
///     alone would put it ahead of a guard or validator registered globally. The stage order has to beat that.
/// </remarks>
[HandlerPriority(int.MinValue)]
internal sealed class TransferAlwaysAnsweringShortcut : ICommandShortcut<TransferCommand>
{
    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(TransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.StagesRun.Add("shortcut");

        return Task.FromResult(Shortcut.Answer("already applied"));
    }
}

/// <summary>
///     Prepares a transfer that is going to be handled.
/// </summary>
internal sealed class TransferPreHandler : ICommandPreHandler<TransferCommand>
{
    /// <inheritdoc />
    public Task PreHandleAsync(TransferCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        message.StagesRun.Add("pre-handler");

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that produces a result, used to exercise refusal mapping.
/// </summary>
internal sealed class QuoteCommand : ICommand<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the caller is permitted to request a quote.
    /// </summary>
    public bool IsPermitted { get; set; } = true;

    /// <summary>
    ///     Gets or sets the symbol to quote, which must be supplied to be well-formed.
    /// </summary>
    public string? Symbol { get; set; } = "ACME";
}

/// <summary>
///     Handles the quote command.
/// </summary>
internal sealed class QuoteCommandHandler : ICommandHandler<QuoteCommand, string>
{
    /// <inheritdoc />
    public Task<string> HandleAsync(QuoteCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("quoted");
    }
}

/// <summary>
///     Refuses a quote the caller is not permitted to request.
/// </summary>
internal sealed class QuotePermissionGuard : ICommandGuard<QuoteCommand>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(QuoteCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.IsPermitted
            ? Verdict.Allow
            : Verdict.Deny("not permitted", "NOT_PERMITTED"));
    }
}

/// <summary>
///     Reports a quote with no symbol.
/// </summary>
internal sealed class QuoteSymbolValidator : ICommandValidator<QuoteCommand>
{
    /// <inheritdoc />
    public Task<Validity> ValidateAsync(QuoteCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(string.IsNullOrWhiteSpace(message.Symbol)
            ? Validity.Invalid("the symbol must be supplied", nameof(QuoteCommand.Symbol), "SYMBOL")
            : Validity.Valid);
    }
}

/// <summary>
///     Maps every refused command that produces a string, registered against the axis rather than one message.
/// </summary>
internal sealed class GlobalCommandRefusalMapper : ICommandRefusalMapper<ICommand, string>
{
    /// <inheritdoc />
    public string Map(ICommand message, Refusal refusal)
    {
        return $"global:{refusal.Outcome}:{refusal.Code ?? "none"}";
    }
}

/// <summary>
///     Maps a refused <see cref="QuoteCommand" />, registered against the concrete message so it wins over the axis one.
/// </summary>
internal sealed class QuoteRefusalMapper : ICommandRefusalMapper<QuoteCommand, string>
{
    /// <inheritdoc />
    public string Map(QuoteCommand message, Refusal refusal)
    {
        return $"quote:{refusal.Outcome}:{refusal.Code ?? "none"}";
    }
}

/// <summary>
///     A second mapper for <see cref="QuoteCommand" />, used to prove an ambiguous registration is reported.
/// </summary>
internal sealed class DuplicateQuoteRefusalMapper : ICommandRefusalMapper<QuoteCommand, string>
{
    /// <inheritdoc />
    public string Map(QuoteCommand message, Refusal refusal)
    {
        return "duplicate";
    }
}

/// <summary>
///     Carries a pipeline marker without implementing any contract that names a message type.
/// </summary>
/// <remarks>
///     Registering this used to succeed and produce a handler that never ran, because every pipeline marker is
///     memberless and the type therefore yields no descriptor.
/// </remarks>
internal sealed class MarkerOnlyPreStageHandler : IMessagePreStageHandler;

/// <summary>
///     Observes how a <see cref="TransferCommand" /> mediation ended.
/// </summary>
internal sealed class TransferCompletionHandler : ICommandCompletionHandler<TransferCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TransferCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public TransferCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<TransferCommand> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("transfer", context.AsUntyped()));

        return Task.CompletedTask;
    }
}

/// <summary>
///     Observes how a <see cref="QuoteCommand" /> mediation ended, including the value a refused caller received.
/// </summary>
internal sealed class QuoteCompletionHandler : ICommandCompletionHandler<QuoteCommand, string>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QuoteCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public QuoteCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<QuoteCommand, string> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("quote", context.AsUntyped()));

        return Task.CompletedTask;
    }
}
