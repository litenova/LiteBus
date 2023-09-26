using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public sealed class ExecutionContext : IExecutionContext
{
    public ExecutionContext(CancellationToken cancellationToken)
    {
        CancellationToken = cancellationToken;
    }

    public CancellationToken CancellationToken { get; }
}