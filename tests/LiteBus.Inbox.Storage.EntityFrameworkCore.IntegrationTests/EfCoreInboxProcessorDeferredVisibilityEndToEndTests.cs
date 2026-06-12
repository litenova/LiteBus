using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

public sealed class EfCoreInboxProcessorDeferredVisibilityEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_deferred_visibility";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxProcessorDeferredVisibilityEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ScheduleAsync_WithVisibleAfter_ShouldDeferProcessingUntilDue()
    {
        var clock = new ManualTimeProvider(EfCoreInboxE2eSupport.BaseTime);
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new CommandRecorder();
        var visibleAfter = EfCoreInboxE2eSupport.BaseTime.AddMinutes(30);

        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<DeferredVisibilityInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Recorder = recorder,
                Clock = clock,
                LeaseOwner = "efcore-inbox-deferred-visibility"
            });

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var orderId = Guid.NewGuid();

        await scheduler.AcceptAsync(InboxAcceptItem<ShipOrderCommand>.From(new ShipOrderCommand {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }, InboxAcceptMetadata.Immediate with
        {
            Visibility = new MessageVisibility.At(visibleAfter)
        }));

        await processor.ProcessPendingAsync();
        recorder.Commands.Should().BeEmpty();

        clock.Advance(TimeSpan.FromMinutes(30));
        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    private sealed class DeferredVisibilityInboxDbContext : EfCoreInboxE2eDbContext
    {
        public DeferredVisibilityInboxDbContext(
            DbContextOptions<DeferredVisibilityInboxDbContext> options,
            EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}