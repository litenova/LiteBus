using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Testing;

/// <summary>
///     Assertion helpers for inbox and outbox messages in tests.
/// </summary>
public static class InboxOutboxAssertions
{
    /// <summary>
    ///     Asserts that the in-memory inbox store contains the expected number of envelopes for a contract name.
    /// </summary>
    /// <param name="store">The in-memory inbox store.</param>
    /// <param name="contractName">The expected contract name.</param>
    /// <param name="expectedCount">The expected number of matching envelopes.</param>
    public static void ShouldContainInboxContract(
        InMemoryInboxStore store,
        string contractName,
        int expectedCount = 1)
    {
        var count = store.GetAll().Count(envelope => envelope.ContractName == contractName);
        if (count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} inbox envelope(s) for contract '{contractName}', found {count}.");
        }
    }

    /// <summary>
    ///     Asserts that the in-memory outbox store contains the expected number of envelopes for a contract name.
    /// </summary>
    /// <param name="store">The in-memory outbox store.</param>
    /// <param name="contractName">The expected contract name.</param>
    /// <param name="expectedCount">The expected number of matching envelopes.</param>
    public static void ShouldContainOutboxContract(
        InMemoryOutboxStore store,
        string contractName,
        int expectedCount = 1)
    {
        var count = store.GetAll().Count(envelope => envelope.ContractName == contractName);
        if (count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Expected {expectedCount} outbox envelope(s) for contract '{contractName}', found {count}.");
        }
    }

    /// <summary>
    ///     Asserts that the in-memory inbox store contains an envelope with the supplied idempotency key.
    /// </summary>
    /// <param name="store">The in-memory inbox store.</param>
    /// <param name="idempotencyKey">The expected idempotency key.</param>
    public static void ShouldContainInboxIdempotencyKey(InMemoryInboxStore store, string idempotencyKey)
    {
        if (!store.GetAll().Any(envelope => envelope.IdempotencyKey == idempotencyKey))
        {
            throw new InvalidOperationException($"No inbox envelope found with idempotency key '{idempotencyKey}'.");
        }
    }

    /// <summary>
    ///     Asserts that the in-memory outbox store contains an envelope with the supplied idempotency key.
    /// </summary>
    /// <param name="store">The in-memory outbox store.</param>
    /// <param name="idempotencyKey">The expected idempotency key.</param>
    public static void ShouldContainOutboxIdempotencyKey(InMemoryOutboxStore store, string idempotencyKey)
    {
        if (!store.GetAll().Any(envelope => envelope.IdempotencyKey == idempotencyKey))
        {
            throw new InvalidOperationException($"No outbox envelope found with idempotency key '{idempotencyKey}'.");
        }
    }
}
