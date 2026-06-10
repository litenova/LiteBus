using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

public sealed class EfCoreInboxProcessorHandlerFailureEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_handler_failure";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxProcessorHandlerFailureEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerFails_ShouldMarkFailedWithVisibleAfter()
    {
        var clock = new ManualTimeProvider(EfCoreInboxE2eSupport.BaseTime);
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<HandlerFailureInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Clock = clock,
                RegisterShipHandler = false,
                RegisterFaultyHandler = true,
                MaxAttempts = 5,
                InitialDelay = TimeSpan.FromSeconds(30),
                LeaseOwner = "efcore-inbox-handler-failure"
            });

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new FaultyCommand());
        await processor.ProcessPendingAsync();

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id);
        row!.Status.Should().Be(InboxStatus.Failed);
        row.LastError.Should().NotBeNullOrWhiteSpace();
        row.VisibleAfter.Should().Be(EfCoreInboxE2eSupport.BaseTime.AddSeconds(30));
    }

    private sealed class HandlerFailureInboxDbContext : EfCoreInboxE2eDbContext
    {
        public HandlerFailureInboxDbContext(DbContextOptions<HandlerFailureInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
