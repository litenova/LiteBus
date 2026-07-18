using LiteBus.Inbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

public sealed class EfCoreInboxProcessorLeaseExpiryEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_lease_expiry";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxProcessorLeaseExpiryEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimAndCompleteCommand()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new CommandRecorder();

        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

        var provider = EfCoreInboxE2eSupport.BuildProvider<LeaseExpiryInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Recorder = recorder,
                LeaseOwner = "efcore-inbox-lease-expiry"
            });
        await using (provider.ConfigureAwait(false))
        {
            var scheduler = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();
            var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

            var orderId = Guid.NewGuid();

            var receipt = await scheduler.AcceptAsync(new ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = "lease-expiry"
            }).ConfigureAwait(false);

            await leaseStore.LeasePendingAsync(new InboxLeaseRequest
            {
                BatchSize = 1,
                LeaseOwner = "stale-worker",
                Now = EfCoreInboxE2eSupport.BaseTime,
                LeaseDuration = TimeSpan.FromMinutes(1)
            }).ConfigureAwait(false);

            await PostgreSqlDatabaseTimeTestSupport.ExpireLeaseAsync(
                _fixture.ConnectionString,
                storeOptions.SchemaName,
                storeOptions.TableName,
                receipt.Id).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Commands.Should().ContainSingle();
            var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id).ConfigureAwait(false);
            row!.Status.Should().Be(InboxStatus.Completed);
            row.AttemptCount.Should().Be(2);
        }
    }

    private sealed class LeaseExpiryInboxDbContext : EfCoreInboxE2eDbContext
    {
        public LeaseExpiryInboxDbContext(DbContextOptions<LeaseExpiryInboxDbContext> options, EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
