using LiteBus.Inbox.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

public sealed class EfCoreInboxProcessorDeadLetterEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_dead_letter";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxProcessorDeadLetterEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMaxAttemptsExceeded_ShouldMoveToDeadLetter()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions).ConfigureAwait(false);

         var provider = EfCoreInboxE2eSupport.BuildProvider<DeadLetterInboxDbContext>(             _fixture.ConnectionString,             storeOptions,             new InboxE2eComposition             {                 RegisterShipHandler = false,                 RegisterFaultyHandler = true,                 MaxAttempts = 1,                 LeaseOwner = "efcore-inbox-dead-letter"             });
         await using (provider.ConfigureAwait(true))
         {

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new FaultyCommand()).ConfigureAwait(false);
        await processor.ProcessPendingAsync().ConfigureAwait(false);

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, receipt.Id).ConfigureAwait(false);
        row!.Status.Should().Be(InboxStatus.DeadLettered);
        row.LastError.Should().NotBeNullOrWhiteSpace();
        }
    }

    private sealed class DeadLetterInboxDbContext : EfCoreInboxE2eDbContext
    {
        public DeadLetterInboxDbContext(DbContextOptions<DeadLetterInboxDbContext> options, EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
