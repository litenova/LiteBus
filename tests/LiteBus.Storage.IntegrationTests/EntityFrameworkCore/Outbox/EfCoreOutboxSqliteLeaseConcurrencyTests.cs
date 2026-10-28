using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Verifies SQLite tracked persist does not overwrite a row claimed by a newer lease.
/// </summary>
public sealed class EfCoreOutboxSqliteLeaseConcurrencyTests : IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxSqliteLeaseConcurrencyTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreOutboxSqliteLeaseConcurrencyTests(SqliteDatabaseFixture fixture)
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
        await EfCoreProviderTestInfrastructure.ResetOutboxTableAsync(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString).ConfigureAwait(false);

        var options = EfCoreProviderTestInfrastructure.OutboxStoreOptions(EfCoreStorageProvider.Sqlite);
        var staleContext = EfCoreProviderTestInfrastructure.CreateOutboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString);
        await using (staleContext.ConfigureAwait(false))
        {
            var store = new EfCoreOutboxStore(_ => Task.FromResult<IOutboxDbContext>(staleContext), options);
            var messageId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await store.AddAsync(new OutboxEnvelope
            {
                Id = messageId,
                ContractName = "tests.events.submitted",
                ContractVersion = 1,
                Payload = """{"orderId":"1"}""",
                CreatedAt = now,
                Status = OutboxStatus.Pending,
                AttemptCount = 0
            }).ConfigureAwait(false);

            var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
            {
                BatchSize = 1,
                LeaseOwner = "worker-a",
                Now = now,
                LeaseDuration = TimeSpan.FromMinutes(5)
            }).ConfigureAwait(false);

            leased.Should().ContainSingle();
            var stale = leased[0];

            var releaser = EfCoreProviderTestInfrastructure.CreateOutboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString);
            await using (releaser.ConfigureAwait(false))
            {
                var row = await releaser.OutboxMessages.SingleAsync(message => message.Id == messageId).ConfigureAwait(false);
                row.LeaseOwner = "worker-b";
                row.LeaseGeneration += 1;
                row.LeaseExpiresAt = now.AddMinutes(5);
                await releaser.SaveChangesAsync().ConfigureAwait(false);
            }

            var result = await store.PersistAsync([stale.AsPublished(now)]).ConfigureAwait(false);
            result.AppliedCount.Should().Be(0);
            result.SkippedCount.Should().Be(1);

            var verification = EfCoreProviderTestInfrastructure.CreateOutboxContext(EfCoreStorageProvider.Sqlite, _fixture.ConnectionString);
            await using (verification.ConfigureAwait(false))
            {
                var stored = await verification.OutboxMessages.SingleAsync(message => message.Id == messageId).ConfigureAwait(false);
                stored.Status.Should().Be(OutboxStatus.Publishing);
                stored.LeaseOwner.Should().Be("worker-b");
                stored.LeaseGeneration.Should().Be(stale.LeaseGeneration + 1);
            }
        }
    }
}
