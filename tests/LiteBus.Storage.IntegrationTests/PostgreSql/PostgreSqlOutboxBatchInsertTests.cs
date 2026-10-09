using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for chunked PostgreSQL outbox batch inserts.
/// </summary>
public sealed class PostgreSqlOutboxBatchInsertTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxBatchInsertTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Confirms a 5,000-envelope batch is stored in input order despite the protocol parameter cap.
    /// </summary>
    [SkippableFact]
    public async Task AddBatchAsync_WithFiveThousandEnvelopes_ShouldStoreEveryEnvelopeInInputOrder()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var now = DateTimeOffset.UtcNow;

        var envelopes = Enumerable.Range(0, 5000)
            .Select(index => new OutboxEnvelope
            {
                Id = Guid.NewGuid(),
                ContractName = "tests.events.bulk",
                ContractVersion = 1,
                Payload = "{}",
                CreatedAt = now.AddTicks(index),
                AttemptCount = 0,
                Status = OutboxStatus.Pending
            })
            .ToArray();

        var results = await store.AddBatchAsync(envelopes).ConfigureAwait(false);

        results.Should().HaveCount(envelopes.Length);

        for (var index = 0; index < envelopes.Length; index++)
        {
            results[index].Envelope.Id.Should().Be(envelopes[index].Id);
            results[index].Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        }

        var counts = await store.GetStatusCountsAsync().ConfigureAwait(false);
        counts[OutboxStatus.Pending].Should().Be(envelopes.Length);
    }
}
