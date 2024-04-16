using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.CreateProduct;

public sealed class CreateProductCommandHandlerPreHandler : ICommandPreHandler<CreateProductCommand>
{
    public Task PreHandleAsync(CreateProductCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        if (message.AbortInPreHandler)
        {
            AmbientExecutionContext.Current!.Abort(new CreateProductCommandResult { CorrelationId = Guid.Empty });
        }

        return Task.CompletedTask;
    }
}