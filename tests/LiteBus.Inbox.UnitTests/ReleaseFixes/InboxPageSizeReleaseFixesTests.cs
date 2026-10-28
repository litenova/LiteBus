using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies inbox message queries reject unbounded page sizes.
/// </summary>
public sealed class InboxPageSizeReleaseFixesTests
{
    /// <summary>
    ///     Verifies the in-memory store rejects a page size above the maximum.
    /// </summary>
    /// <returns>A task that completes when the bound assertion succeeds.</returns>
    [Fact]
    public async Task QueryAsync_WhenPageSizeExceedsMaximum_ShouldThrow()
    {
        var store = new InMemoryInboxStore();

        var act = () => store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = InboxMessagePageRequest.MaxPageSize + 1 });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies the in-memory store still accepts the maximum page size.
    /// </summary>
    /// <returns>A task that completes when the bound assertion succeeds.</returns>
    [Fact]
    public async Task QueryAsync_WhenPageSizeIsMaximum_ShouldNotThrow()
    {
        var store = new InMemoryInboxStore();

        var page = await store.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = InboxMessagePageRequest.MaxPageSize }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies the inbox manager rejects a page size above the maximum before reaching the store.
    /// </summary>
    /// <returns>A task that completes when the bound assertion succeeds.</returns>
    [Fact]
    public async Task ManagerQueryAsync_WhenPageSizeExceedsMaximum_ShouldThrow()
    {
        var manager = CreateManager(new InMemoryInboxStore());

        var act = () => manager.QueryAsync(
            new InboxMessageFilter(),
            new InboxMessagePageRequest { PageSize = int.MaxValue });

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Creates an inbox manager over the supplied in-memory store.
    /// </summary>
    /// <param name="store">The in-memory inbox store.</param>
    /// <returns>The configured inbox manager.</returns>
    private static InboxManager CreateManager(InMemoryInboxStore store)
    {
        var cleanupOptions = new InboxCleanupHostOptions();

        return new InboxManager(
            store,
            store,
            new InboxRetentionCoordinator(cleanupOptions),
            cleanupOptions,
            TimeProvider.System);
    }
}
