using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Storage.Testing;

namespace LiteBus.Inbox.Storage.InMemory.UnitTests;

/// <summary>
///     Runs retention contract tests against <see cref="InMemoryInboxStore" />.
/// </summary>
public sealed class InboxRetentionStoreContractTests : global::LiteBus.Storage.Testing.InboxRetentionStoreContractTests
{
    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        var store = new InMemoryInboxStore();
        return new InboxStoreRoles(store, store, store, store, store);
    }
}
