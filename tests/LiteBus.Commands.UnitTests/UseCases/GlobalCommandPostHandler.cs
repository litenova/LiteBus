using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases;

public sealed class GlobalCommandPostHandler : ICommandPostHandler
{
    public Task PostHandleAsync(ICommand message, object messageResult, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableCommand auditableCommand)
        {
            auditableCommand.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}