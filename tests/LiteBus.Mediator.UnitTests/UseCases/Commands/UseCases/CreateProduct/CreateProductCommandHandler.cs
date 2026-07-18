using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.CreateProduct;

public sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, CreateProductCommandResult>
{
    public Task<CreateProductCommandResult> HandleAsync(CreateProductCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.FromResult(new CreateProductCommandResult
        {
            CorrelationId = message.CorrelationId
        });
    }
}