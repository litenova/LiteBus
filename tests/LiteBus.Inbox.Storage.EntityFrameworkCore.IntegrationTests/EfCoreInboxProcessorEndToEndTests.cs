using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     End-to-end inbox processor tests with Entity Framework Core storage and PostgreSQL.
/// </summary>
public sealed class EfCoreInboxProcessorEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_happy_path";

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxProcessorEndToEndTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxProcessorEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that <see cref="IInboxProcessor.ProcessPendingAsync" /> executes a scheduled command through EF Core storage.
    /// </summary>
    /// <returns>A task that completes when the command is handled.</returns>
    [Fact]
    public async Task ProcessPendingAsync_ShouldExecuteScheduledCommandThroughEfCoreStore()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        var recorder = new CommandRecorder();

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<HappyPathInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                Recorder = recorder,
                LeaseOwner = "efcore-inbox-happy-path"
            });

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);
    }

    private sealed class HappyPathInboxDbContext : EfCoreInboxE2eDbContext
    {
        public HappyPathInboxDbContext(DbContextOptions<HappyPathInboxDbContext> options, EfCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}
