using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.OpenGenericAssemblyScan;

/// <summary>
///     An open generic post-handler constrained to <see cref="IOpenGenericScanTestCommand" />.
///     It lives in the same assembly as the tests, so <c>RegisterFromAssembly</c> should discover it automatically.
/// </summary>
public sealed class ScanTestOpenGenericPostHandler<TCommand> : ICommandPostHandler<TCommand>
    where TCommand : ICommand, IOpenGenericScanTestCommand
{
    public Task PostHandleAsync(TCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        if (message is IOpenGenericScanTestCommand auditable)
        {
            auditable.ExecutedTypes.Add(typeof(ScanTestOpenGenericPostHandler<TCommand>));
        }

        return Task.CompletedTask;
    }
}
