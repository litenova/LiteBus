using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CreateProduct;

[HandlerOrder(1)]
public sealed class CreateProductCommandHandlerPostHandler1 : ICommandPostHandler<CreateProductCommand, CreateProductCommandResult>
{
    public Task PostHandleAsync(CreateProductCommand message, CreateProductCommandResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}