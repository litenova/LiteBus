using LiteBus.Commands.Abstractions;
using LiteBus.Saga.Abstractions;

namespace LiteBus.Samples.V6.Saga;

/// <summary>
///     Advances order saga state through <see cref="ISagaContext" /> during inbox dispatch.
/// </summary>
public sealed class AdvanceOrderSagaCommandHandler : ICommandHandler<AdvanceOrderSagaCommand>
{
    /// <summary>
    ///     Gets the ambient saga context for the active inbox dispatch.
    /// </summary>
    private readonly ISagaContext _sagaContext;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AdvanceOrderSagaCommandHandler" /> class.
    /// </summary>
    /// <param name="sagaContext">The ambient saga context.</param>
    public AdvanceOrderSagaCommandHandler(ISagaContext sagaContext)
    {
        _sagaContext = sagaContext;
    }

    /// <inheritdoc />
    public Task HandleAsync(AdvanceOrderSagaCommand command, CancellationToken cancellationToken = default)
    {
        if (_sagaContext.IsActive)
        {
            var state = _sagaContext.GetState<OrderSagaState>();
            state.Step++;
            _sagaContext.SetState(state);
        }

        return Task.CompletedTask;
    }
}