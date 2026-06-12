using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using LiteBus.DurableTransport.IntegrationTesting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.DurableTransport.IntegrationTests.Kafka;

/// <summary>
///     Kafka-specific helpers for durable transport integration tests.
/// </summary>
internal static class KafkaTransportTestInfrastructure
{
    /// <summary>
    ///     Creates a unique topic name for the current test run.
    /// </summary>
    /// <param name="prefix">The prefix identifying the scenario under test.</param>
    /// <returns>A topic name safe for Kafka routing.</returns>
    public static string CreateTopic(string prefix)
    {
        return $"litebus-kafka-{prefix}-{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Ensures the supplied topics exist before consumers subscribe or producers publish.
    /// </summary>
    /// <param name="bootstrapServers">The bootstrap servers list.</param>
    /// <param name="topics">The topic names to create when missing.</param>
    /// <returns>A task that completes when the topics are available.</returns>
    public static async Task EnsureTopicsExistAsync(string bootstrapServers, params string[] topics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        ArgumentNullException.ThrowIfNull(topics);

        if (topics.Length == 0)
        {
            return;
        }

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(15));

        var existing = metadata.Topics
            .Select(topic => topic.Topic)
            .ToHashSet(StringComparer.Ordinal);

        var specifications = topics
            .Where(topic => !existing.Contains(topic))
            .Select(topic => new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            })
            .ToList();

        if (specifications.Count == 0)
        {
            return;
        }

        try
        {
            await admin.CreateTopicsAsync(specifications).ConfigureAwait(false);
        }
        catch (CreateTopicsException exception)
        {
            foreach (var result in exception.Results)
            {
                if (result.Error.Code is not ErrorCode.NoError and not ErrorCode.TopicAlreadyExists)
                {
                    throw new KafkaException(result.Error);
                }
            }
        }
    }

    /// <summary>
    ///     Consumes one record from the supplied topic using a dedicated consumer group.
    /// </summary>
    /// <param name="bootstrapServers">The bootstrap servers list.</param>
    /// <param name="topic">The topic to read from.</param>
    /// <param name="timeout">The maximum time to wait for a record.</param>
    /// <returns>The consumed transport-shaped message.</returns>
    public static async Task<(string Body, IReadOnlyDictionary<string, object?> Headers)> ConsumeOneAsync(
        string bootstrapServers,
        string topic,
        TimeSpan timeout)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"litebus-verify-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(250));

            if (result is null)
            {
                continue;
            }

            var headers = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var header in result.Message.Headers)
            {
                headers[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
            }

            return (Encoding.UTF8.GetString(result.Message.Value), headers);
        }

        throw new TimeoutException($"No Kafka record was received from topic '{topic}' within {timeout}.");
    }

    /// <summary>
    ///     Disposes a test service provider while tolerating Kafka client flush races during teardown.
    /// </summary>
    /// <param name="provider">The service provider created for a Kafka integration test.</param>
    /// <returns>A task that completes when disposal finishes or is skipped safely.</returns>
    public static async ValueTask DisposeProviderSafelyAsync(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        try
        {
            await provider.DisposeAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    ///     Waits until the supplied store reports the expected pending inbox count.
    /// </summary>
    /// <param name="countPending">The function returning the current pending inbox count.</param>
    /// <param name="expectedCount">The expected pending count.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the count matches.</returns>
    public static Task WaitForPendingInboxCountAsync(
        Func<Task<int>> countPending,
        int expectedCount,
        TimeSpan timeout)
    {
        return PollingWait.UntilAsync(async () => await countPending().ConfigureAwait(false) == expectedCount, timeout);
    }

    /// <summary>
    ///     Waits until the supplied consumer group commits at least the expected offset for partition zero.
    /// </summary>
    /// <param name="bootstrapServers">The bootstrap servers list.</param>
    /// <param name="groupId">The consumer group identifier.</param>
    /// <param name="topic">The topic name.</param>
    /// <param name="expectedCommittedOffset">The minimum committed offset.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the committed offset reaches the expected value.</returns>
    public static async Task WaitForCommittedOffsetAsync(
        string bootstrapServers,
        string groupId,
        string topic,
        long expectedCommittedOffset,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        var partition = new TopicPartition(topic, 0);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var committed = consumer.Committed([partition], TimeSpan.FromSeconds(5));

            if (committed.Count > 0 && committed[0].Offset != Offset.Unset && committed[0].Offset.Value >= expectedCommittedOffset)
            {
                return;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Consumer group '{groupId}' did not commit offset {expectedCommittedOffset} on topic '{topic}' within {timeout}.");
    }

    /// <summary>
    ///     Waits until the supplied store count remains stable for a short observation window.
    /// </summary>
    /// <param name="countStore">The function returning the current inbox store count.</param>
    /// <param name="expectedCount">The expected stable count.</param>
    /// <param name="stableDuration">The duration the count must remain unchanged.</param>
    /// <param name="timeout">The maximum time to wait for stability.</param>
    /// <returns>A task that completes when the count has remained stable.</returns>
    public static async Task WaitForStableStoreCountAsync(
        Func<int> countStore,
        int expectedCount,
        TimeSpan stableDuration,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(countStore);

        var deadline = DateTime.UtcNow + timeout;
        DateTime? stableSince = null;

        while (DateTime.UtcNow < deadline)
        {
            if (countStore() == expectedCount)
            {
                stableSince ??= DateTime.UtcNow;

                if (DateTime.UtcNow - stableSince >= stableDuration)
                {
                    return;
                }
            }
            else
            {
                stableSince = null;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Store count did not remain stable at {expectedCount} for {stableDuration} within {timeout}.");
    }
}
