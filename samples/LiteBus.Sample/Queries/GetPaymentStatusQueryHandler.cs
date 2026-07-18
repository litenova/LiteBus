using LiteBus.Queries.Abstractions;

namespace LiteBus.Sample.Queries;

/// <summary>
///     Reads payment status from the sample ledger without modifying state.
/// </summary>
public sealed class GetPaymentStatusQueryHandler : IQueryHandler<GetPaymentStatusQuery, PaymentStatus?>
{
    /// <summary>
    ///     The sample payment state store.
    /// </summary>
    private readonly PaymentLedger _ledger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GetPaymentStatusQueryHandler" /> class.
    /// </summary>
    /// <param name="ledger">The sample payment state store.</param>
    public GetPaymentStatusQueryHandler(PaymentLedger ledger)
    {
        _ledger = ledger;
    }

    /// <inheritdoc />
    public Task<PaymentStatus?> HandleAsync(
        GetPaymentStatusQuery message,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_ledger.Find(message.PaymentId));
    }
}
