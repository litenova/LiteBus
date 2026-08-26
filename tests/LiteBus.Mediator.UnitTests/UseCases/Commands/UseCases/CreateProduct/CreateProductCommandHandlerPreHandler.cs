using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.CreateProduct;

public sealed class CreateProductCommandHandlerPreHandler : ICommandGate<CreateProductCommand, CreateProductCommandResult>
{
    public Task<PipelineDirective<CreateProductCommandResult>> DecideAsync(
        CreateProductCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ExecutedTypes.Add(GetType());

        if (message.ShortCircuitInGate)
        {
            return Task.FromResult(PipelineDirective<CreateProductCommandResult>.ShortCircuit(
                new CreateProductCommandResult { CorrelationId = Guid.Empty },
                "answered by the gate"));
        }

        return Task.FromResult(PipelineDirective<CreateProductCommandResult>.Continue);
    }
}
