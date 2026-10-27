using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies the in-memory store answers like a durable one: in-flight claims, fingerprint comparison, and takeover
///     after the claim timeout.
/// </summary>
[Collection("Sequential")]
public sealed class InMemoryIdempotencyStoreTests : LiteBusTestBase
{
    [Fact]
    public async Task A_second_claim_while_unsettled_answers_in_flight()
    {
        var store = new InMemoryIdempotencyStore();

        var first = await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);
        var second = await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);

        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);
        second.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);
    }

    [Fact]
    public async Task A_claim_older_than_the_timeout_is_taken_over()
    {
        var time = new TestTimeProvider();
        var store = new InMemoryIdempotencyStore(time, TimeSpan.FromMinutes(5));

        var first = await store.TryClaimAsync("key-1", "fingerprint-a").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        // A different fingerprint does not matter while the claim is unsettled: the key is held either way.
        var racing = await store.TryClaimAsync("key-1", "fingerprint-b").ConfigureAwait(false);
        racing.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);

        time.Advance(TimeSpan.FromMinutes(6));

        var taken = await store.TryClaimAsync("key-1", "fingerprint-b").ConfigureAwait(false);
        taken.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        // The takeover stored the new caller's fingerprint with the refreshed claim.
        await store.CompleteAsync("key-1", payload: null).ConfigureAwait(false);

        var replay = await store.TryClaimAsync("key-1", "fingerprint-b").ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);

        var stale = await store.TryClaimAsync("key-1", "fingerprint-a").ConfigureAwait(false);
        stale.Outcome.Should().Be(IdempotencyClaimOutcome.FingerprintMismatch);
    }

    [Fact]
    public async Task An_applied_key_with_a_matching_fingerprint_replays_the_payload()
    {
        var store = new InMemoryIdempotencyStore();

        await store.TryClaimAsync("key-1", "fingerprint-a").ConfigureAwait(false);
        await store.CompleteAsync("key-1", """{"receipt":"r-1"}""").ConfigureAwait(false);

        var replay = await store.TryClaimAsync("key-1", "fingerprint-a").ConfigureAwait(false);

        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("""{"receipt":"r-1"}""");
    }

    [Fact]
    public async Task An_applied_key_with_a_different_fingerprint_mismatches()
    {
        var store = new InMemoryIdempotencyStore();

        await store.TryClaimAsync("key-1", "fingerprint-a").ConfigureAwait(false);
        await store.CompleteAsync("key-1", payload: null).ConfigureAwait(false);

        var replay = await store.TryClaimAsync("key-1", "fingerprint-b").ConfigureAwait(false);

        replay.Outcome.Should().Be(IdempotencyClaimOutcome.FingerprintMismatch);
    }

    [Fact]
    public async Task A_repeat_without_a_fingerprint_still_replays_a_fingerprinted_key()
    {
        var store = new InMemoryIdempotencyStore();

        await store.TryClaimAsync("key-1", "fingerprint-a").ConfigureAwait(false);
        await store.CompleteAsync("key-1", "payload").ConfigureAwait(false);

        // Either side missing a fingerprint means no comparison is possible, so the repeat is answered.
        var replay = await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);

        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("payload");
    }

    [Fact]
    public async Task A_fingerprinted_repeat_still_replays_a_key_completed_without_one()
    {
        var store = new InMemoryIdempotencyStore();

        await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);
        await store.CompleteAsync("key-1", "payload").ConfigureAwait(false);

        var replay = await store.TryClaimAsync("key-1", "fingerprint-b").ConfigureAwait(false);

        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("payload");
    }

    [Fact]
    public async Task Release_frees_an_unsettled_claim_but_never_an_applied_key()
    {
        var store = new InMemoryIdempotencyStore();

        await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);
        await store.ReleaseAsync("key-1").ConfigureAwait(false);

        var retry = await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);
        retry.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        await store.CompleteAsync("key-1", payload: null).ConfigureAwait(false);
        await store.ReleaseAsync("key-1").ConfigureAwait(false);

        var replay = await store.TryClaimAsync("key-1", fingerprint: null).ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);

        store.AppliedKeys.Should().Contain("key-1");
    }

    [Fact]
    public void The_claim_timeout_defaults_to_five_minutes()
    {
        new InMemoryIdempotencyStore().ClaimTimeout.Should().Be(TimeSpan.FromMinutes(5));
    }
}

/// <summary>
///     A clock the test moves by hand, so claim timeouts are deterministic.
/// </summary>
internal sealed class TestTimeProvider : TimeProvider
{
    /// <summary>
    ///     The current time.
    /// </summary>
    private DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }

    /// <summary>
    ///     Moves the clock forward.
    /// </summary>
    /// <param name="delta">How far to move the clock.</param>
    public void Advance(TimeSpan delta)
    {
        _now += delta;
    }
}
