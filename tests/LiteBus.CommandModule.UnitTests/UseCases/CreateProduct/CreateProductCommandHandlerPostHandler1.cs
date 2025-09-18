using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.CreateProduct;

[HandlerPriority(1)]
public sealed class CreateProductCommandHandlerPostHandler1 : ICommandPostHandler<CreateProductCommand, CreateProductCommandResult>
{
    public Task PostHandleAsync(CreateProductCommand message, CreateProductCommandResult? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}