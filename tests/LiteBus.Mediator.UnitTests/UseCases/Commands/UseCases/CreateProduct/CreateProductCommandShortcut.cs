using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.CreateProduct;

public sealed class CreateProductCommandShortcut : ICommandShortcut<CreateProductCommand, CreateProductCommandResult>
{
    public Task<Shortcut<CreateProductCommandResult>> TryAnswerAsync(
        CreateProductCommand message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        message.ExecutedTypes.Add(GetType());

        if (message.AnswerFromShortcut)
        {
            return Task.FromResult(Shortcut<CreateProductCommandResult>.Answer(
                new CreateProductCommandResult { CorrelationId = Guid.Empty },
                "answered by the shortcut"));
        }

        return Task.FromResult(Shortcut<CreateProductCommandResult>.None);
    }
}
