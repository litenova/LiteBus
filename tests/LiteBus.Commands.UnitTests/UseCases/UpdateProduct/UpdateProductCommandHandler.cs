using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.UpdateProduct;

public sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>
{
    public Task HandleAsync(UpdateProductCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());

        return Task.CompletedTask;
    }
}