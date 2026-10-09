namespace LiteBus.Storage.UnitTests.InMemory.Inbox;

/// <summary>
///     Runs shared inbox store contract tests against <see cref="InMemoryInboxStore" />.
/// </summary>
public sealed class InboxStoreContractTests : LiteBus.Storage.Testing.InboxStoreContractTests
{
    /// <inheritdoc />
    protected override InboxStoreContractRoles CreateStore()
    {
        var store = new InMemoryInboxStore();
        return new InboxStoreContractRoles(store, store, store, store, store, store, store, store);
    }
}