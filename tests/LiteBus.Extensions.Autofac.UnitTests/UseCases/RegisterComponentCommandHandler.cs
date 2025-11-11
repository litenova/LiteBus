using LiteBus.Commands.Abstractions;

namespace LiteBus.Extensions.Autofac.UnitTests.UseCases;

/// <summary>
///     The main handler for <see cref="RegisterComponentCommand" />.
/// </summary>
public sealed class RegisterComponentCommandHandler : ICommandHandler<RegisterComponentCommand>
{
    public Task HandleAsync(RegisterComponentCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedHandlers.Add(GetType());
        return Task.CompletedTask;
    }
}