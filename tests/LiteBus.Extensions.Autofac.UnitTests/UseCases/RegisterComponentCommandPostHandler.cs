using LiteBus.Commands.Abstractions;

namespace LiteBus.Extensions.Autofac.UnitTests.UseCases;

/// <summary>
/// A post-handler for <see cref="RegisterComponentCommand"/>.
/// </summary>
public sealed class RegisterComponentCommandPostHandler : ICommandPostHandler<RegisterComponentCommand>
{
    public Task PostHandleAsync(RegisterComponentCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedHandlers.Add(GetType());
        return Task.CompletedTask;
    }
}