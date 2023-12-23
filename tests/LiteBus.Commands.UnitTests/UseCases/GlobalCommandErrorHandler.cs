using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases;

public class GlobalCommandErrorHandler : ICommandErrorHandler
{
    public Task HandleErrorAsync(ICommand message, object? messageResult, Exception exception, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableCommand auditableCommand)
        {
            auditableCommand.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}