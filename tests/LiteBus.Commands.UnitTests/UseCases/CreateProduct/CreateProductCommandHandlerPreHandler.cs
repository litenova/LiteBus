using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CreateProduct;

public sealed class CreateProductCommandHandlerPreHandler : ICommandPreHandler<CreateProductCommand>
{
    public Task PreHandleAsync(CreateProductCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}