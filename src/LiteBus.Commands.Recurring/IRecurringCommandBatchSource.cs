using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Produces the commands one pass of a recurring batch loop sends as a single batch.
/// </summary>
/// <remarks>
///     <para>
///         Registered as a scoped service by
///         <see cref="CommandModuleBuilderRecurringExtensions.AddRecurringBatch{TSource}(CommandModuleBuilder, System.Action{RecurringCommandHostOptions})" />
///         and resolved from a fresh dispatch scope on every pass, so an implementation can take scoped dependencies such
///         as a database context. The scope is disposed after the pass.
///     </para>
///     <para>
///         The source reads in its own scope, outside the batch's transaction, so the rows it read can change before the
///         batch commits. Commands must still check the state they change, and the lease, not the transaction, keeps two
///         replicas from producing the same batch.
///     </para>
/// </remarks>
/// <example>
///     <code><![CDATA[
/// public sealed class DueInvoiceBatchSource : IRecurringCommandBatchSource
/// {
///     private readonly BillingContext _context;
///
///     public DueInvoiceBatchSource(BillingContext context)
///     {
///         _context = context;
///     }
///
///     public async Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
///     {
///         var due = await _context.Invoices
///             .Where(invoice => invoice.DueDate <= DateTimeOffset.UtcNow)
///             .Take(50)
///             .ToListAsync(cancellationToken)
///             .ConfigureAwait(false);
///
///         return RecurringCommandBatch.From(
///             due.Select(static invoice => (ICommand) new SendInvoiceReminderCommand(invoice.Id)).ToList(),
///             due.Count >= 50);
///     }
/// }
/// ]]></code>
/// </example>
public interface IRecurringCommandBatchSource
{
    /// <summary>
    ///     Creates the batch sent on one pass of the loop.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the read when the host shuts down or the lease is lost.</param>
    /// <returns>
    ///     The commands the pass sends as one batch, or <see cref="RecurringCommandBatch.Empty" /> when there is
    ///     nothing to send.
    /// </returns>
    Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken);
}
