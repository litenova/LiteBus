using LiteBus.Commands.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.UseCases.LogActivity;

public class LogActivityCommandPreHandler<TPayload> : ICommandPreHandler<LogActivityCommand<TPayload>>
{
    public Task PreHandleAsync(LogActivityCommand<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}