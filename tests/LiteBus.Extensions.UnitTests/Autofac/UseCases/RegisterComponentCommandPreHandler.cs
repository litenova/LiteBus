using LiteBus.Commands.Abstractions;

namespace LiteBus.Extensions.UnitTests.Autofac.UseCases;

/// <summary>
///     A pre-handler for <see cref="RegisterComponentCommand" />.
/// </summary>
public sealed class RegisterComponentCommandPreHandler : ICommandPreHandler<RegisterComponentCommand>
{
    public Task PreHandleAsync(RegisterComponentCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedHandlers.Add(GetType());
        return Task.CompletedTask;
    }
}
