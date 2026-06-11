namespace LiteBus.Inbox.Storage.InMemory.UnitTests;

/// <summary>
///     Runs shared inbox store contract tests against <see cref="InMemoryInboxStore" />.
/// </summary>
public sealed class InboxStoreContractTests : LiteBus.Storage.Testing.InboxStoreContractTests
{
    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        var store = new InMemoryInboxStore();
        return new InboxStoreRoles(store, store, store, store, store, store, store, store);
    }
}