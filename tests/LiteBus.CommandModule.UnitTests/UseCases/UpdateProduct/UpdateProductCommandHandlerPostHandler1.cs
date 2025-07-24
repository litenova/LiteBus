using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.UpdateProduct;

[HandlerOrder(1)]
public sealed class UpdateProductCommandHandlerPostHandler1 : ICommandPostHandler<UpdateProductCommand>
{
    public Task PostHandleAsync(UpdateProductCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}