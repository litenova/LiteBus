using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

internal sealed class AlwaysFailingOutboxDispatcher : IOutboxDispatcher
{
    public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Simulated dispatcher failure.");
    }
}