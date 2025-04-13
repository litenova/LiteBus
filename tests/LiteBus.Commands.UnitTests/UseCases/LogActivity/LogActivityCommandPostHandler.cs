using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.LogActivity;

public class LogActivityCommandPostHandler<TPayload> : ICommandPostHandler<LogActivityCommand<TPayload>>
{
    public Task PostHandleAsync(LogActivityCommand<TPayload> message, object? messageResult, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}