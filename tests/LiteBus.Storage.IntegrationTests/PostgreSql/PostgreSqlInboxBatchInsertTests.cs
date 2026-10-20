using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for chunked PostgreSQL inbox batch inserts.
/// </summary>
public sealed class PostgreSqlInboxBatchInsertTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The fixture that owns the shared PostgreSQL container.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxBatchInsertTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlInboxBatchInsertTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms a 4,000-envelope batch is stored in input order despite the protocol parameter cap.
    /// </summary>
    [SkippableFact]
    public async Task AddBatchAsync_WithFourThousandEnvelopes_ShouldStoreEveryEnvelopeInInputOrder()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;

        var envelopes = Enumerable.Range(0, 4000)
            .Select(index => new InboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "tests.commands.bulk",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = now.AddTicks(index),
                AttemptCount = 0,
                Status = InboxStatus.Pending
            })
            .ToArray();

        var results = await store.AddBatchAsync(envelopes).ConfigureAwait(false);

        results.Should().HaveCount(envelopes.Length);

        for (var index = 0; index < envelopes.Length; index++)
        {
            results[index].Envelope.Id.Should().Be(envelopes[index].Id);
            results[index].Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        }

        var counts = await store.GetStatusCountsAsync().ConfigureAwait(false);
        counts[InboxStatus.Pending].Should().Be(envelopes.Length);
    }
}
