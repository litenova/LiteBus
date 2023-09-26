#nullable enable

using System.Threading;

namespace LiteBus.Messaging.Abstractions;

public interface IExecutionContext
{
    CancellationToken CancellationToken { get; }
}