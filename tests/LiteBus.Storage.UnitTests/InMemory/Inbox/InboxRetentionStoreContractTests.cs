namespace LiteBus.Storage.UnitTests.InMemory.Inbox;

/// <summary>
///     Runs retention contract tests against <see cref="InMemoryInboxStore" />.
/// </summary>
public sealed class InboxRetentionStoreContractTests : LiteBus.Storage.Testing.InboxRetentionStoreContractTests

{
    /// <inheritdoc />
    protected override InboxRetentionStoreContractRoles CreateStore()

    {
        var store = new InMemoryInboxStore();

        return new InboxRetentionStoreContractRoles(store, store, store, store, store);
    }
}