using LiteBus.Inbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

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
        var clock = new ManualTimeProvider(EfCoreInboxE2eSupport.BaseTime);
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new CommandRecorder();

        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<LeaseExpiryInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Recorder = recorder,
                Clock = clock,
                LeaseOwner = "efcore-inbox-lease-expiry"
            });

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var leaseStore = provider.GetRequiredService<IInboxLeaseStore>();

        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AcceptAsync(new ShipOrderCommand {
            OrderId = orderId,
            IdempotencyKey = "lease-expiry"
        });

        await leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = EfCoreInboxE2eSupport.BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        clock.Advance(TimeSpan.FromMinutes(1));
        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle();
        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id);
        row!.Status.Should().Be(InboxStatus.Completed);
        row.AttemptCount.Should().Be(2);
    }

    private sealed class LeaseExpiryInboxDbContext : EfCoreInboxE2eDbContext
    {
        public LeaseExpiryInboxDbContext(DbContextOptions<LeaseExpiryInboxDbContext> options, EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}