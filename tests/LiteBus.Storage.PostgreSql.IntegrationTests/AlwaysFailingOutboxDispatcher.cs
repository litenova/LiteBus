using LiteBus.Outbox.Abstractions;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal sealed class AlwaysFailingOutboxDispatcher : IOutboxDispatcher
{
    public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated dispatcher failure.");
    }
}
