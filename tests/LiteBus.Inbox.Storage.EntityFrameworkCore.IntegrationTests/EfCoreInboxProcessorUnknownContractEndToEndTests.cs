using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IInboxProcessor = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

public sealed class EfCoreInboxProcessorUnknownContractEndToEndTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private const string TableName = "inbox_ef_unknown_contract";

    private readonly PostgreSqlFixture _fixture;

    public EfCoreInboxProcessorUnknownContractEndToEndTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenContractIsUnknown_ShouldMarkFailedInDatabase()
    {
        var storeOptions = EfCoreInboxE2eSupport.CreateStoreOptions(TableName);
        await EfCoreInboxE2eSupport.EnsureInboxTableAsync(_fixture.ConnectionString, storeOptions);

        await using var provider = EfCoreInboxE2eSupport.BuildProvider<UnknownContractInboxDbContext>(
            _fixture.ConnectionString,
            storeOptions,
            new InboxE2eComposition
            {
                RegisterShipHandler = false,
                LeaseOwner = "efcore-inbox-unknown-contract"
            });

        var processor = provider.GetRequiredService<IInboxProcessor>();
        var writer = provider.GetRequiredService<IInboxStore>();
        var commandId = Guid.NewGuid();

        await writer.EnqueueAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "unknown.contract",
            ContractVersion = 99,
            Payload = "{}",
            CreatedAt = EfCoreInboxE2eSupport.BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        });

        await processor.ProcessPendingAsync();

        var row = await EfCoreInboxTableReaders.ReadInboxAsync(_fixture.ConnectionString, storeOptions, commandId);
        row!.Status.Should().Be(InboxStatus.Failed);
        row.LastError.Should().Contain(nameof(MessageContractNotRegisteredException));
    }

    private sealed class UnknownContractInboxDbContext : EfCoreInboxE2eDbContext
    {
        public UnknownContractInboxDbContext(
            DbContextOptions<UnknownContractInboxDbContext> options,
            EntityFrameworkCoreInboxStoreOptions storeOptions)
            : base(options, storeOptions)
        {
        }
    }
}