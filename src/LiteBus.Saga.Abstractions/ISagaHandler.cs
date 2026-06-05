using LiteBus.Commands.Abstractions;

namespace LiteBus.Saga.Abstractions;

/// <summary>
///     Handles one command while reading and mutating correlated saga state.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TState">The saga state type.</typeparam>
/// <remarks>
///     Handlers can implement this interface instead of combining <see cref="ICommandHandler{TCommand}" /> with
///     <see cref="ISaga{TState}" /> when they prefer an explicit saga entry point.
/// </remarks>
public interface ISagaHandler<in TCommand, TState>
    where TCommand : ICommand
    where TState : class, new()
{
    /// <summary>
    ///     Handles the command while mutating saga state exposed through <paramref name="saga" />.
    /// </summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="saga">The active saga state accessor for the current correlation.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes when handling finishes.</returns>
    Task HandleAsync(TCommand command, ISaga<TState> saga, CancellationToken cancellationToken = default);
}
