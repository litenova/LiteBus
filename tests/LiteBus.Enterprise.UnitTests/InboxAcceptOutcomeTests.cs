using AwesomeAssertions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;

namespace LiteBus.Enterprise.UnitTests;

/// <summary>
///     Verifies inbox acceptance receipts report duplicate outcomes and honor idempotency conflict modes.
/// </summary>
public sealed class InboxAcceptOutcomeTests
{
    /// <summary>
    ///     Confirms duplicate idempotency keys return <see cref="InboxAcceptOutcome.AlreadyAccepted" />.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WhenDuplicateIdempotencyKey_ShouldReturnAlreadyAcceptedOutcome()
    {
        var store = new InMemoryInboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<OutcomeTestCommand>("test-command");
        var serializer = new SystemTextJsonMessageSerializer();

        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System));

        const string idempotencyKey = "idem-42";

        var first = await inbox.AcceptAsync(
            InboxAcceptItem<OutcomeTestCommand>.WithIdempotency(new OutcomeTestCommand { Value = "first" }, idempotencyKey));

        var second = await inbox.AcceptAsync(
            InboxAcceptItem<OutcomeTestCommand>.WithIdempotency(new OutcomeTestCommand { Value = "second" }, idempotencyKey));

        first.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        second.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        second.Id.Should().Be(first.Id);
        store.Count.Should().Be(1);
    }

    /// <summary>
    ///     Confirms strict idempotency mode rejects duplicate keys.
    /// </summary>
    [Fact]
    public async Task AcceptAsync_WhenStrictIdempotencyConflicts_ShouldThrow()
    {
        var store = new InMemoryInboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<OutcomeTestCommand>("test-command");
        var serializer = new SystemTextJsonMessageSerializer();

        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System));

        const string idempotencyKey = "strict-idem";
        var strictMetadata = InboxAcceptMetadata.Immediate with
        {
            Idempotency = new Idempotency.Keyed(idempotencyKey, IdempotencyConflictMode.Strict)
        };

        await inbox.AcceptAsync(
            InboxAcceptItem<OutcomeTestCommand>.From(new OutcomeTestCommand { Value = "first" }, strictMetadata)).ConfigureAwait(true);


        var act = () => inbox.AcceptAsync(
            InboxAcceptItem<OutcomeTestCommand>.From(new OutcomeTestCommand { Value = "second" }, strictMetadata));

        await act.Should().ThrowAsync<IdempotencyConflictException>();
    }

    /// <summary>
    ///     Test command payload.
    /// </summary>
    private sealed class OutcomeTestCommand
    {
        /// <summary>
        ///     Gets or sets the value.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }
}
