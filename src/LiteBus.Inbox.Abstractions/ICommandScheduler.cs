using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Accepts commands into storage for later execution by an inbox processor.
/// </summary>
/// <remarks>
///     <para>
///         Use this API when the current caller should receive an acceptance receipt instead of waiting for the command
///         handler to run. `ICommandMediator.SendAsync` executes commands immediately; `ScheduleAsync` records a command
///         envelope and returns after the backing store accepts it.
///     </para>
///     <para>
///         Scheduled commands must implement <see cref="ICommand" />. Commands with handler results are not valid for
///         deferred execution because the future processor cannot return that result to the original caller. Use a query
///         to read final state after the command has run.
///     </para>
///     <para>
///         Register each scheduled command type in `IMessageContractRegistry` with a stable name and version. Closed
///         generic command types are supported when each closed shape is registered. Open generic contract definitions
///         are rejected because the persisted payload must map back to one concrete CLR type.
///     </para>
/// </remarks>
public interface ICommandScheduler
{
    /// <summary>
    ///     Stores a command for later execution by an inbox processor.
    /// </summary>
    /// <typeparam name="TCommand">The command type being scheduled. The runtime type is used for contract lookup.</typeparam>
    /// <param name="command">The command instance to serialize and store.</param>
    /// <param name="options">
    ///     Optional command metadata such as a caller-supplied command id, idempotency key, first visible timestamp,
    ///     correlation id, causation id, and tenant id. Metadata stays outside the command payload.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel serialization or the store write.</param>
    /// <returns>
    ///     A receipt containing the command id, contract name, version, acceptance time, and trace metadata.
    ///     The receipt is not the command handler result.
    /// </returns>
    /// <exception cref="System.ArgumentException">
    ///     Thrown when the runtime type of <paramref name="command" /> implements <c>ICommand&lt;TResult&gt;</c>. The
    ///     inbox processor cannot return a handler result to the original caller, so result-bearing commands must not
    ///     be scheduled for deferred execution.
    /// </exception>
    Task<CommandReceipt<TCommand>> ScheduleAsync<TCommand>(
        TCommand command,
        CommandScheduleOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;
}