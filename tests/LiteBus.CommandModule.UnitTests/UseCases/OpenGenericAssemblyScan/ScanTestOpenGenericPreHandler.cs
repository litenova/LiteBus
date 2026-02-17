using LiteBus.Commands.Abstractions;

namespace LiteBus.CommandModule.UnitTests.UseCases.OpenGenericAssemblyScan;

/// <summary>
///     An open generic pre-handler constrained to <see cref="IOpenGenericScanTestCommand" />.
///     It lives in the same assembly as the tests, so <c>RegisterFromAssembly</c> should discover it automatically.
/// </summary>
public sealed class ScanTestOpenGenericPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand, IOpenGenericScanTestCommand
{
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        message.ExecutedTypes.Add(typeof(ScanTestOpenGenericPreHandler<TCommand>));
        return Task.CompletedTask;
    }
}
