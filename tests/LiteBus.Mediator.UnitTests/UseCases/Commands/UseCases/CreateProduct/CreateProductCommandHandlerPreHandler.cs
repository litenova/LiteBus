using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.CreateProduct;

public sealed class CreateProductCommandHandlerPreHandler : ICommandShortCircuitingPreHandler<CreateProductCommand>
{
    public Task<PipelineDirective> PreHandleAsync(CreateProductCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ExecutedTypes.Add(GetType());

        if (message.AbortInPreHandler)
        {
            return Task.FromResult(PipelineDirective.ShortCircuit(
                new CreateProductCommandResult { CorrelationId = Guid.Empty },
                "aborted by pre-handler"));
        }

        return Task.FromResult(PipelineDirective.Continue);
    }
}
