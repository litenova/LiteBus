using LiteBus.Commands.Storage.Marten;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using LiteBus.Testing;
using Marten;

namespace LiteBus.Storage.IntegrationTests.PostgreSql.Marten;

/// <summary>
///     Integration tests for the Marten idempotency store: claim, replay, fingerprint, in-flight, takeover, and concurrent claim behavior against a PostgreSQL container.
/// </summary>
public sealed class MartenIdempotencyStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenIdempotencyStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenIdempotencyStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Claim_ShouldGrantANewKey()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);

        var claim = await store.TryClaimAsync($"idem:{Guid.NewGuid():N}", "fp-1").ConfigureAwait(false);

        claim.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);
    }

    [SkippableFact]
    public async Task ClaimCompleteClaim_ShouldGrantOnceThenReplayTheStoredPayload()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);
        first.ClaimToken.Should().NotBeNullOrWhiteSpace();

        await store.CompleteAsync(key, first.ClaimToken!, """{"receipt":"r-1"}""").ConfigureAwait(false);

        var repeat = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        repeat.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        repeat.Payload.Should().Be("""{"receipt":"r-1"}""");
    }

    [SkippableFact]
    public async Task CompletedKey_WithDifferentFingerprint_ShouldReportMismatch()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        await store.CompleteAsync(key, first.ClaimToken!, "receipt").ConfigureAwait(false);

        var repeat = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);

        repeat.Outcome.Should().Be(IdempotencyClaimOutcome.FingerprintMismatch);
    }

    [SkippableFact]
    public async Task CompletedKey_WithNullFingerprintOnEitherSide_ShouldReplay()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var claimedWithoutFingerprint = $"idem:{Guid.NewGuid():N}";
        var replayedWithoutFingerprint = $"idem:{Guid.NewGuid():N}";

        var firstClaim = await store.TryClaimAsync(claimedWithoutFingerprint, null).ConfigureAwait(false);
        await store.CompleteAsync(claimedWithoutFingerprint, firstClaim.ClaimToken!, "receipt-a").ConfigureAwait(false);

        var secondClaim = await store.TryClaimAsync(replayedWithoutFingerprint, "fp-1").ConfigureAwait(false);
        await store.CompleteAsync(replayedWithoutFingerprint, secondClaim.ClaimToken!, "receipt-b").ConfigureAwait(false);

        var first = await store.TryClaimAsync(claimedWithoutFingerprint, "fp-9").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        first.Payload.Should().Be("receipt-a");

        var second = await store.TryClaimAsync(replayedWithoutFingerprint, null).ConfigureAwait(false);
        second.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        second.Payload.Should().Be("receipt-b");
    }

    [SkippableFact]
    public async Task UnsettledClaim_ShouldAnswerInFlightRegardlessOfFingerprint()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        var same = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        same.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);

        var different = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);
        different.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);
    }

    [SkippableFact]
    public async Task ExpiredClaim_ShouldBeTakenOverByTheNextClaimant()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new MartenIdempotencyStore(documentStore, new MartenIdempotencyStoreOptions(), clock);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        clock.Advance(TimeSpan.FromMinutes(6));

        var takeover = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);
        takeover.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        await store.CompleteAsync(key, takeover.ClaimToken!, "receipt").ConfigureAwait(false);

        var replay = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("receipt");

        var stale = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        stale.Outcome.Should().Be(IdempotencyClaimOutcome.FingerprintMismatch);
    }

    [SkippableFact]
    public async Task Release_ShouldFreeAnUnsettledClaimButNeverAnAppliedKey()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        await store.ReleaseAsync(key, first.ClaimToken!).ConfigureAwait(false);

        var retry = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        retry.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        await store.CompleteAsync(key, retry.ClaimToken!, null).ConfigureAwait(false);
        await store.ReleaseAsync(key, retry.ClaimToken!).ConfigureAwait(false);

        var repeat = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        repeat.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        repeat.Payload.Should().BeNull();
    }

    [SkippableFact]
    public async Task ConcurrentClaims_ShouldGrantExactlyOneAndHoldTheOtherInFlight()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var key = $"idem:{Guid.NewGuid():N}";

        await WarmUpSchemaAsync(store).ConfigureAwait(false);

        var outcomes = await Task.WhenAll(
            ClaimAsync(store, key, "fp-a"),
            ClaimAsync(store, key, "fp-b")).ConfigureAwait(false);

        outcomes.Should().ContainSingle(outcome => outcome == IdempotencyClaimOutcome.Granted);
        outcomes.Should().ContainSingle(outcome => outcome == IdempotencyClaimOutcome.InFlight);
    }

    [SkippableFact]
    public async Task ConcurrentTakeovers_ShouldGrantExactlyOne()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new MartenIdempotencyStore(documentStore, new MartenIdempotencyStoreOptions(), clock);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-old").ConfigureAwait(false);
        first.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        clock.Advance(TimeSpan.FromMinutes(6));

        var outcomes = await Task.WhenAll(
            ClaimAsync(store, key, "fp-a"),
            ClaimAsync(store, key, "fp-b")).ConfigureAwait(false);

        outcomes.Should().ContainSingle(outcome => outcome == IdempotencyClaimOutcome.Granted);
        outcomes.Should().ContainSingle(outcome => outcome == IdempotencyClaimOutcome.InFlight);
    }

    [SkippableFact]
    public async Task LateSettle_AfterTakeover_ShouldLeaveTheNewClaimantAlone()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var store = new MartenIdempotencyStore(documentStore, new MartenIdempotencyStoreOptions(), clock);
        var key = $"idem:{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(6));

        var taken = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);
        taken.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);

        // The superseded claimant settles late with the token it was granted.
        await store.CompleteAsync(key, first.ClaimToken!, "stale-payload").ConfigureAwait(false);
        await store.ReleaseAsync(key, first.ClaimToken!).ConfigureAwait(false);

        // The new claimant still holds the key: neither marked applied nor freed.
        var racing = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);
        racing.Outcome.Should().Be(IdempotencyClaimOutcome.InFlight);

        await store.CompleteAsync(key, taken.ClaimToken!, "fresh-payload").ConfigureAwait(false);

        var replay = await store.TryClaimAsync(key, "fp-2").ConfigureAwait(false);
        replay.Outcome.Should().Be(IdempotencyClaimOutcome.AlreadyCompleted);
        replay.Payload.Should().Be("fresh-payload");
    }

    [SkippableFact]
    public async Task Complete_OnAMissingKey_ShouldNotInsertAnAppliedDocument()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var store = new MartenIdempotencyStore(documentStore);
        var key = $"idem:{Guid.NewGuid():N}";

        await store.CompleteAsync(key, Guid.NewGuid().ToString("N"), "payload").ConfigureAwait(false);

        // No applied document was left behind, so the key is still claimable.
        var claim = await store.TryClaimAsync(key, "fp-1").ConfigureAwait(false);
        claim.Outcome.Should().Be(IdempotencyClaimOutcome.Granted);
    }

    [SkippableFact]
    public async Task Constructor_WithNonPositiveClaimTimeout_ShouldThrow()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);

        var action = () => new MartenIdempotencyStore(
            documentStore,
            new MartenIdempotencyStoreOptions { ClaimTimeout = TimeSpan.Zero });

        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_commands_idem_tests";
        });
    }

    private static async Task WarmUpSchemaAsync(MartenIdempotencyStore store)
    {
        var warmupKey = $"idem:warmup:{Guid.NewGuid():N}";
        var warmup = await store.TryClaimAsync(warmupKey, null).ConfigureAwait(false);
        await store.ReleaseAsync(warmupKey, warmup.ClaimToken!).ConfigureAwait(false);
    }

    private static async Task<IdempotencyClaimOutcome> ClaimAsync(MartenIdempotencyStore store, string key, string? fingerprint)
    {
        var claim = await store.TryClaimAsync(key, fingerprint).ConfigureAwait(false);
        return claim.Outcome;
    }
}
