using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases.LogActivity;

public class LogActivityCommandHandler<TPayload> : ICommandHandler<LogActivityCommand<TPayload>>
{
    public Task HandleAsync(LogActivityCommand<TPayload> message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(GetType());
        return Task.CompletedTask;
    }
}