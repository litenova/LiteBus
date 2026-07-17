using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

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
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        var recorder = new CommandRecorder();
        var visibleAfter = DateTimeOffset.UtcNow.AddHours(1);

        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

        var provider = EfCoreInboxE2eSupport.BuildProvider<DeferredVisibilityInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Recorder = recorder,
                LeaseOwner = "efcore-inbox-deferred-visibility"
            });
        await using (provider.ConfigureAwait(false))
        {
            var scheduler = provider.GetRequiredService<IInbox>();
            var processor = provider.GetRequiredService<IInboxProcessor>();

            var orderId = Guid.NewGuid();

            var receipt = await scheduler.AcceptAsync(InboxAcceptItem<ShipOrderCommand>.From(
                new ShipOrderCommand
                {
                    OrderId = orderId,
                    IdempotencyKey = $"ship:{orderId}"
                },
                InboxAcceptMetadata.Immediate with
                {
                    Visibility = new MessageVisibility.At(visibleAfter)
                })).ConfigureAwait(false);

            await processor.ProcessPendingAsync().ConfigureAwait(false);
            recorder.Commands.Should().BeEmpty();

            await PostgreSqlDatabaseTimeTestSupport.MakeVisibleAsync(
                _fixture.ConnectionString,
                storeOptions.SchemaName,
                storeOptions.TableName,
                receipt.Id).ConfigureAwait(false);
            await processor.ProcessPendingAsync().ConfigureAwait(false);

            recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
        }
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
