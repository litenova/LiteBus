using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Storage.Testing;

namespace LiteBus.Outbox.Storage.InMemory.UnitTests;

/// <summary>
///     Runs retention contract tests against <see cref="InMemoryOutboxStore" />.
/// </summary>
public sealed class OutboxRetentionStoreContractTests : global::LiteBus.Storage.Testing.OutboxRetentionStoreContractTests
{
    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        var store = new InMemoryOutboxStore();
        return new OutboxStoreContracts(store, store, store, store, store);
    }
}
