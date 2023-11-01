using LiteBus.Commands.Abstractions;

namespace LiteBus.Commands.UnitTests.UseCases;

public sealed class GlobalCommandPreHandler : ICommandPreHandler
{
    public Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        if (message is IAuditableCommand auditableCommand)
        {
            auditableCommand.ExecutedTypes.Add(GetType());
        }

        return Task.CompletedTask;
    }
}