using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.CreateProduct;

[HandlerPriority(2)]
public sealed class CreateProductCommandHandlerPostHandler2 : ICommandPostHandler<CreateProductCommand>
{
    public Task PostHandleAsync(CreateProductCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}