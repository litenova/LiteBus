using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.UpdateProduct;

public sealed class UpdateProductCommandHandlerPreHandler : ICommandPreHandler<UpdateProductCommand>
{
    public Task PreHandleAsync(UpdateProductCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}