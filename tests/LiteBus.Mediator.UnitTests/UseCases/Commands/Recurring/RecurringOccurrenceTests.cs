using LiteBus.Commands.Recurring;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers recorded occurrences: stable occurrence keys and the in-memory occurrence store behind catch-up
///     decisions.
/// </summary>
public sealed class RecurringOccurrenceTests
{
    [Fact]
    public void Occurrence_key_without_tenant_combines_the_loop_and_the_instant()
    {
        var key = DefaultRecurringOccurrenceKey.Instance.CreateKey(
            "close-carts",
            null,
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));

        key.Should().Be("close-carts/20260105T090000Z");
    }

    [Fact]
    public void Occurrence_key_with_tenant_combines_the_loop_the_tenant_and_the_instant()
    {
        var key = DefaultRecurringOccurrenceKey.Instance.CreateKey(
            "close-carts",
            "acme",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));

        key.Should().Be("close-carts/acme/20260105T090000Z");
    }

    [Fact]
    public void Occurrence_key_is_stable_across_zones_for_the_same_instant()
    {
        var left = DefaultRecurringOccurrenceKey.Instance.CreateKey(
            "close-carts",
            "acme",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));
        var right = DefaultRecurringOccurrenceKey.Instance.CreateKey(
            "close-carts",
            "acme",
            new DateTimeOffset(2026, 1, 5, 11, 0, 0, TimeSpan.FromHours(2)));

        right.Should().Be(left);
    }

    [Fact]
    public void Occurrence_key_rejects_a_blank_loop()
    {
        var act = () => DefaultRecurringOccurrenceKey.Instance.CreateKey(
            "  ",
            "acme",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Store_records_an_occurrence_and_returns_it_as_last()
    {
        var store = new InMemoryRecurringOccurrenceStore();
        var occurrence = new RecurringOccurrence(
            "close-carts",
            "acme",
            "close-carts/acme/20260105T090000Z",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 9, 0, 1, TimeSpan.Zero));

        var recorded = await store.TryRecordAsync(occurrence, CancellationToken.None).ConfigureAwait(false);
        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);

        recorded.Should().BeTrue();
        last.Should().Be(occurrence);
    }

    [Fact]
    public async Task Store_rejects_a_duplicate_key()
    {
        var store = new InMemoryRecurringOccurrenceStore();
        var occurrence = new RecurringOccurrence(
            "close-carts",
            "acme",
            "close-carts/acme/20260105T090000Z",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 9, 0, 1, TimeSpan.Zero));

        var first = await store.TryRecordAsync(occurrence, CancellationToken.None).ConfigureAwait(false);
        var second = await store.TryRecordAsync(occurrence with { }, CancellationToken.None).ConfigureAwait(false);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task Store_tracks_loops_and_tenants_independently()
    {
        var store = new InMemoryRecurringOccurrenceStore();
        var sweep = new RecurringOccurrence(
            "close-carts",
            "acme",
            "close-carts/acme/20260105T090000Z",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 9, 0, 1, TimeSpan.Zero));
        var single = new RecurringOccurrence(
            "cleanup",
            null,
            "cleanup/20260105T090000Z",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 9, 0, 2, TimeSpan.Zero));

        await store.TryRecordAsync(sweep, CancellationToken.None).ConfigureAwait(false);
        await store.TryRecordAsync(single, CancellationToken.None).ConfigureAwait(false);

        var sweepLast = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);
        var singleLast = await store.GetLastAsync("cleanup", null, CancellationToken.None).ConfigureAwait(false);
        var missing = await store.GetLastAsync("close-carts", "other", CancellationToken.None).ConfigureAwait(false);

        sweepLast.Should().Be(sweep);
        singleLast.Should().Be(single);
        missing.Should().BeNull();
    }

    [Fact]
    public async Task Store_returns_null_for_a_loop_with_no_record()
    {
        var store = new InMemoryRecurringOccurrenceStore();

        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);

        last.Should().BeNull();
    }

    [Fact]
    public async Task Store_keeps_the_latest_recorded_occurrence_as_last()
    {
        var store = new InMemoryRecurringOccurrenceStore();
        var earlier = new RecurringOccurrence(
            "close-carts",
            "acme",
            "close-carts/acme/20260105T090000Z",
            new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 9, 0, 2, TimeSpan.Zero));
        var later = new RecurringOccurrence(
            "close-carts",
            "acme",
            "close-carts/acme/20260105T100000Z",
            new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 5, 10, 0, 1, TimeSpan.Zero));

        // Recorded out of order, as two replicas may: the later recording still wins.
        await store.TryRecordAsync(later, CancellationToken.None).ConfigureAwait(false);
        await store.TryRecordAsync(earlier, CancellationToken.None).ConfigureAwait(false);

        var last = await store.GetLastAsync("close-carts", "acme", CancellationToken.None).ConfigureAwait(false);

        last.Should().Be(later);
    }

    [Fact]
    public async Task Store_rejects_a_null_occurrence()
    {
        var store = new InMemoryRecurringOccurrenceStore();

        var act = async () => await store.TryRecordAsync(null!, CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Store_rejects_a_blank_loop_on_read()
    {
        var store = new InMemoryRecurringOccurrenceStore();

        var act = async () => await store.GetLastAsync("  ", "acme", CancellationToken.None).ConfigureAwait(false);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }
}
