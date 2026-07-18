namespace LiteBus.Storage.UnitTests.InMemory.Outbox;

/// <summary>
///     Runs shared outbox store contract tests against <see cref="InMemoryOutboxStore" />.
/// </summary>
public sealed class OutboxStoreContractTests : LiteBus.Storage.Testing.OutboxStoreContractTests
{
    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        var store = new InMemoryOutboxStore();
        return new OutboxStoreContracts(store, store, store, store, store, store, store, store);
    }
}