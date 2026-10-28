using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies SQLite tracked persist does not overwrite a row claimed by a newer lease.
/// </summary>
public sealed class EfCoreInboxSqliteLeaseConcurrencyTests : IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxSqliteLeaseConcurrencyTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreInboxSqliteLeaseConcurrencyTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a persist racing a newer lease reports skipped and keeps the new holder.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task PersistAsync_WhenRowWasReclaimedAfterTrackedLoad_ShouldSkipInsteadOfOverwriting()
    {
        await EfCoreProviderTestInfrastructure.ResetInboxTableAsync(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString).ConfigureAwait(false);

        var options = EfCoreProviderTestInfrastructure.InboxStoreOptions(EfCoreStorageProvider.Sqlite);
        var staleContext = EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString);
        await using (staleContext.ConfigureAwait(false))
        {
            var store = new EfCoreInboxStore(_ => Task.FromResult<IInboxDbContext>(staleContext), options);
            var messageId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await store.AddAsync(new InboxEnvelope
            {
                Id = messageId,
                ContractName = "tests.place",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = now,
                Status = InboxStatus.Pending,
                AttemptCount = 0
            }).ConfigureAwait(false);

            var leased = await store.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 1,
                LeaseOwner = "worker-a",
                Now = now,
                LeaseDuration = TimeSpan.FromMinutes(5)
            }).ConfigureAwait(false);

            leased.Should().ContainSingle();
            var stale = leased[0];

            var releaser = EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString);
            await using (releaser.ConfigureAwait(false))
            {
                var row = await releaser.InboxMessages.SingleAsync(message => message.Id == messageId).ConfigureAwait(false);
                row.LeaseOwner = "worker-b";
                row.LeaseGeneration += 1;
                row.LeaseExpiresAt = now.AddMinutes(5);
                await releaser.SaveChangesAsync().ConfigureAwait(false);
            }

            var result = await store.PersistAsync([stale.AsCompleted()]).ConfigureAwait(false);
            result.AppliedCount.Should().Be(0);
            result.SkippedCount.Should().Be(1);

            var verification = EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString);
            await using (verification.ConfigureAwait(false))
            {
                var stored = await verification.InboxMessages.SingleAsync(message => message.Id == messageId).ConfigureAwait(false);
                stored.Status.Should().Be(InboxStatus.Processing);
                stored.LeaseOwner.Should().Be("worker-b");
                stored.LeaseGeneration.Should().Be(stale.LeaseGeneration + 1);
            }
        }
    }
}
