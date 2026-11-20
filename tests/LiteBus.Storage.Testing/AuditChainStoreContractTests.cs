using LiteBus.Auditing;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared contract tests for audit chain store implementations.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type of the store under test.</typeparam>
/// <remarks>
///     <para>
///         Each store's integration test class derives from this base with its own document type and proves the same
///         <see cref="IAuditChainStore{TDocument}" /> behavior: conditional head appends under contention, unlinked
///         staging in commit order, paged reads in ascending sequence order, head listing by filter, checkpoint
///         publish-state transitions, and erasure by filter. The store treats hashes, schemes, and signatures as
///         opaque values; what they mean is verified by the chain engine, not here.
///     </para>
///     <para>
///         A derived class creates a store isolated for one test case and maps an opaque marker string onto its
///         document's payload, so the base can assert order and round-trips without knowing the document shape.
///     </para>
/// </remarks>
public abstract class AuditChainStoreContractTests<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the UTC timestamp used as the baseline for opened-at and checkpoint assertions.
    /// </summary>
    protected virtual DateTimeOffset BaseTime { get; } = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>
    ///     Creates a chain store isolated for one test case.
    /// </summary>
    /// <returns>The store under test, starting empty.</returns>
    /// <remarks>
    ///     The returned store must support concurrent calls, because the contention tests append through it from
    ///     several tasks at once. A store backed by an ambient session or connection scopes that session per
    ///     operation.
    /// </remarks>
    protected abstract IAuditChainStore<TDocument> CreateStore();

    /// <summary>
    ///     Creates a stored audit document carrying the given marker in its payload.
    /// </summary>
    /// <param name="marker">The opaque marker the base reads back with <see cref="GetMarker" />.</param>
    /// <returns>A document with its chain position unset.</returns>
    /// <remarks>
    ///     The base sets the chain fields (<see cref="IAuditChainDocument.Segment" />,
    ///     <see cref="IAuditChainDocument.Sequence" />, <see cref="IAuditChainDocument.PreviousHash" />,
    ///     <see cref="IAuditChainDocument.Hash" />, and <see cref="IAuditChainDocument.HashScheme" />) after this
    ///     returns. The factory only maps the marker onto the provider's payload shape.
    /// </remarks>
    protected abstract TDocument CreateDocument(string marker);

    /// <summary>
    ///     Reads the marker a document was created with.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The marker <see cref="CreateDocument" /> stored on the document.</returns>
    protected abstract string GetMarker(TDocument document);

    /// <summary>
    ///     Verifies that reading the head of a segment with no head returns null.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ReadHeadAsync_WhenSegmentHasNoHead_ShouldReturnNull()
    {
        var store = CreateStore();

        var head = await store.ReadHeadAsync("orders", CancellationToken.None).ConfigureAwait(false);

        head.Should().BeNull();
    }

    /// <summary>
    ///     Verifies that the first append to a segment creates the head and stores the linked documents.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AppendAsync_WhenSegmentHasNoHead_ShouldCreateHeadAndStoreDocuments()
    {
        var store = CreateStore();
        const string segment = "orders";
        var expected = CreateHead(segment, version: 0, lastSequence: 0, lastHash: null, count: 0);
        TDocument[] documents =
        [
            CreateLinkedDocument(segment, "record-1", 1, null, HashFor(1)),
            CreateLinkedDocument(segment, "record-2", 2, HashFor(1), HashFor(2)),
        ];
        var updated = CreateHead(segment, version: 1, lastSequence: 2, lastHash: HashFor(2), count: 2);

        await store.AppendAsync(segment, expected, documents, updated, CancellationToken.None).ConfigureAwait(false);

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        AssertHeadMatches(head!, updated);

        var stored = await CollectAsync(
            store.ReadSegmentAsync(segment, 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        stored.Should().HaveCount(2);
        stored[0].Sequence.Should().Be(1);
        stored[1].Sequence.Should().Be(2);
        GetMarker(stored[0]).Should().Be("record-1");
        GetMarker(stored[1]).Should().Be("record-2");
        stored[0].Hash.Should().Be(HashFor(1));
        stored[1].Hash.Should().Be(HashFor(2));
        stored[0].PreviousHash.Should().BeNull();
        stored[1].PreviousHash.Should().Be(HashFor(1));
        stored.Should().OnlyContain(document => document.Segment == segment);
        stored.Should().OnlyContain(document => document.HashScheme == AuditHashChainOptions.DefaultSchemeName);

        var unlinked = await CollectAsync(
            store.ListUnlinkedAsync(segment, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unlinked.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that an append with a stale expected head version throws and stores nothing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AppendAsync_WhenExpectedVersionIsStale_ShouldThrowAndStoreNothing()
    {
        var store = CreateStore();
        const string segment = "orders";
        var first = await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        var stale = CreateHead(segment, version: 0, lastSequence: 0, lastHash: null, count: 0);
        TDocument[] documents = [CreateLinkedDocument(segment, "record-2", 2, first.LastHash, HashFor(2))];
        var updated = first with
        {
            LastSequence = 2,
            LastHash = HashFor(2),
            Count = 2,
            Version = 2,
        };

        var append = async () =>
        {
            await store.AppendAsync(segment, stale, documents, updated, CancellationToken.None).ConfigureAwait(false);
        };

        await append.Should().ThrowAsync<AuditHeadConflictException>().ConfigureAwait(false);

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        AssertHeadMatches(head!, first);

        var stored = await CollectAsync(
            store.ReadSegmentAsync(segment, 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        stored.Should().ContainSingle();
        GetMarker(stored[0]).Should().Be("record-1");
    }

    /// <summary>
    ///     Verifies that concurrent appends to one segment assign unique sequences with no gaps and leave the head on
    ///     the last record.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    /// <remarks>
    ///     The losing appends retry sequentially, the way linkers do after a conflict, so the segment converges on
    ///     one document per sequence.
    /// </remarks>
    [SkippableFact]
    public async Task AppendAsync_ConcurrentAppends_ShouldAssignUniqueSequencesWithoutGaps()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        var storedHead = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        storedHead.Should().NotBeNull();
        var expected = storedHead!;

        const int racerCount = 4;
        var attempts = new Task<bool>[racerCount];
        for (var index = 0; index < racerCount; index++)
        {
            var marker = "racer-" + index;
            var document = CreateLinkedDocument(segment, marker, 2, expected.LastHash, "hash-" + marker);
            var updated = expected with
            {
                LastSequence = 2,
                LastHash = document.Hash,
                Count = expected.Count + 1,
                Version = expected.Version + 1,
            };
            attempts[index] = TryAppendAsync(store, segment, expected, document, updated);
        }

        await Task.WhenAll(attempts).ConfigureAwait(false);

        var winners = 0;
        foreach (var attempt in attempts)
        {
            if (await attempt.ConfigureAwait(false))
            {
                winners++;
            }
        }

        winners.Should().Be(1);

        var afterRace = await CollectAsync(
            store.ReadSegmentAsync(segment, 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        afterRace.Should().HaveCount(2);

        var storedMarkers = afterRace.Select(GetMarker).ToHashSet(StringComparer.Ordinal);
        var missing = Enumerable.Range(0, racerCount)
            .Select(index => "racer-" + index)
            .Where(marker => !storedMarkers.Contains(marker))
            .ToArray();
        missing.Should().HaveCount(racerCount - 1);

        await AppendMarkersAsync(store, segment, missing).ConfigureAwait(false);

        var documents = await CollectAsync(
            store.ReadSegmentAsync(segment, 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        documents.Should().HaveCount(5);
        for (var index = 0; index < documents.Count; index++)
        {
            documents[index].Sequence.Should().Be(index + 1);
        }

        GetMarker(documents[0]).Should().Be("record-1");
        documents.Select(GetMarker).Should().BeEquivalentTo(["record-1", "racer-0", "racer-1", "racer-2", "racer-3"]);

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.LastSequence.Should().Be(5);
        head.Count.Should().Be(5);
        head.LastHash.Should().Be(documents[^1].Hash);
        head.Version.Should().Be(3, "the head version counts head writes: the first append, the winning racer and the batch of the missing racers");
    }

    /// <summary>
    ///     Verifies that appends to one segment leave the heads and documents of other segments untouched.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AppendAsync_ShouldIsolateSegments()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "orders", ["order-1"]).ConfigureAwait(false);
        await AppendMarkersAsync(store, "payments", ["payment-1", "payment-2"]).ConfigureAwait(false);

        var ordersHead = await store.ReadHeadAsync("orders", CancellationToken.None).ConfigureAwait(false);
        var paymentsHead = await store.ReadHeadAsync("payments", CancellationToken.None).ConfigureAwait(false);
        ordersHead.Should().NotBeNull();
        paymentsHead.Should().NotBeNull();
        ordersHead!.LastSequence.Should().Be(1);
        ordersHead.Count.Should().Be(1);
        paymentsHead!.LastSequence.Should().Be(2);
        paymentsHead.Count.Should().Be(2);

        var orders = await CollectAsync(
            store.ReadSegmentAsync("orders", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        var payments = await CollectAsync(
            store.ReadSegmentAsync("payments", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        orders.Should().ContainSingle();
        GetMarker(orders[0]).Should().Be("order-1");
        payments.Should().HaveCount(2);
        GetMarker(payments[0]).Should().Be("payment-1");
        GetMarker(payments[1]).Should().Be("payment-2");
    }

    /// <summary>
    ///     Verifies that staged documents list in commit order with no chain position, no head, and no linked reads.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task StageAsync_ShouldListUnlinkedInCommitOrder()
    {
        var store = CreateStore();
        const string segment = "orders";
        string[] markers = ["staged-1", "staged-2", "staged-3"];
        foreach (var marker in markers)
        {
            await store.StageAsync(segment, CreateDocument(marker), CancellationToken.None).ConfigureAwait(false);
        }

        var unlinked = await CollectAsync(
            store.ListUnlinkedAsync(segment, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unlinked.Should().HaveCount(markers.Length);
        for (var index = 0; index < markers.Length; index++)
        {
            GetMarker(unlinked[index]).Should().Be(markers[index]);
        }

        unlinked.Should().OnlyContain(document => document.Sequence == 0);
        unlinked.Should().OnlyContain(document => document.Hash == null);
        unlinked.Should().OnlyContain(document => document.PreviousHash == null);
        unlinked.Should().OnlyContain(document => document.HashScheme == null);
        unlinked.Should().OnlyContain(document => document.Segment == segment);

        // A store may write an empty placeholder head when it stages, so the link sweep finds staged-only segments.
        // The port allows either shape, but staging never advances the chain.
        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);

        if (head is not null)
        {
            head.Count.Should().Be(0);
            head.LastSequence.Should().Be(0);
            head.LastHash.Should().BeNull();
        }

        var linked = await CollectAsync(
            store.ReadSegmentAsync(segment, 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        linked.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that the unlinked age filter returns every staged document with a null age and none with an age
    ///     older than any staged document.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListUnlinkedAsync_WhenOlderThanExcludesRecentStages_ShouldReturnOnlyOlderDocuments()
    {
        var store = CreateStore();
        const string segment = "orders";
        await store.StageAsync(segment, CreateDocument("staged-1"), CancellationToken.None).ConfigureAwait(false);

        var older = await CollectAsync(
            store.ListUnlinkedAsync(segment, TimeSpan.FromDays(30), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        var all = await CollectAsync(
            store.ListUnlinkedAsync(segment, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        older.Should().BeEmpty();
        all.Should().ContainSingle();
        GetMarker(all[0]).Should().Be("staged-1");
    }

    /// <summary>
    ///     Verifies that the unlinked listing excludes documents the chain linked.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListUnlinkedAsync_ShouldExcludeLinkedDocuments()
    {
        var store = CreateStore();
        const string segment = "orders";
        await store.StageAsync(segment, CreateDocument("staged-1"), CancellationToken.None).ConfigureAwait(false);
        await AppendMarkersAsync(store, segment, ["linked-1", "linked-2"]).ConfigureAwait(false);

        var unlinked = await CollectAsync(
            store.ListUnlinkedAsync(segment, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        unlinked.Should().ContainSingle();
        GetMarker(unlinked[0]).Should().Be("staged-1");
    }

    /// <summary>
    ///     Verifies that a staged-only segment is listed once no matter how many documents it holds, including a
    ///     segment that has no head yet.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListUnlinkedSegmentsAsync_WhenSegmentHoldsOnlyStagedDocuments_ShouldListItOnce()
    {
        var store = CreateStore();
        await store.StageAsync("orders", CreateDocument("staged-1"), CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync("orders", CreateDocument("staged-2"), CancellationToken.None).ConfigureAwait(false);
        await store.StageAsync("payments", CreateDocument("staged-3"), CancellationToken.None).ConfigureAwait(false);

        var segments = await CollectAsync(
            store.ListUnlinkedSegmentsAsync(null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        segments.Should().BeEquivalentTo(["orders", "payments"]);
    }

    /// <summary>
    ///     Verifies that a segment holding only linked documents is excluded from the unlinked listing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListUnlinkedSegmentsAsync_WhenSegmentHoldsOnlyLinkedDocuments_ShouldExcludeIt()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "linked", ["record-1", "record-2"]).ConfigureAwait(false);
        await store.StageAsync("staged", CreateDocument("staged-1"), CancellationToken.None).ConfigureAwait(false);

        var segments = await CollectAsync(
            store.ListUnlinkedSegmentsAsync(null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        segments.Should().BeEquivalentTo(["staged"]);
    }

    /// <summary>
    ///     Verifies that the unlinked age filter returns every segment holding a staged document with a null age
    ///     and no segment whose staged documents are all younger than the age.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListUnlinkedSegmentsAsync_WhenOlderThanExcludesRecentStages_ShouldReturnOnlyOlderSegments()
    {
        var store = CreateStore();
        await store.StageAsync("orders", CreateDocument("staged-1"), CancellationToken.None).ConfigureAwait(false);

        var older = await CollectAsync(
            store.ListUnlinkedSegmentsAsync(TimeSpan.FromDays(30), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        var all = await CollectAsync(
            store.ListUnlinkedSegmentsAsync(null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        older.Should().BeEmpty();
        all.Should().BeEquivalentTo(["orders"]);
    }

    /// <summary>
    ///     Verifies that segment reads return linked documents in ascending sequence order for full, sliced, and
    ///     suffix ranges, and return nothing past the head or for an unknown segment.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ReadSegmentAsync_ShouldReturnAscendingPages()
    {
        var store = CreateStore();
        const string segment = "orders";
        var markers = Enumerable.Range(1, 10).Select(index => "record-" + index).ToArray();
        await AppendMarkersAsync(store, segment, markers).ConfigureAwait(false);

        var full = await CollectAsync(
            store.ReadSegmentAsync(segment, 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        full.Should().HaveCount(markers.Length);
        for (var index = 0; index < markers.Length; index++)
        {
            full[index].Sequence.Should().Be(index + 1);
            GetMarker(full[index]).Should().Be(markers[index]);
        }

        var slice = await CollectAsync(
            store.ReadSegmentAsync(segment, 4, 6, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        slice.Should().HaveCount(3);
        slice[0].Sequence.Should().Be(4);
        slice[1].Sequence.Should().Be(5);
        slice[2].Sequence.Should().Be(6);
        GetMarker(slice[0]).Should().Be("record-4");
        GetMarker(slice[2]).Should().Be("record-6");

        var suffix = await CollectAsync(
            store.ReadSegmentAsync(segment, 8, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        suffix.Should().HaveCount(3);
        suffix[0].Sequence.Should().Be(8);
        suffix[2].Sequence.Should().Be(10);

        var beyond = await CollectAsync(
            store.ReadSegmentAsync(segment, 11, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        beyond.Should().BeEmpty();

        var unknown = await CollectAsync(
            store.ReadSegmentAsync("unknown", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unknown.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a large segment reads through successive pages with no gaps and no duplicates.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ReadSegmentAsync_LargeSegment_ShouldPageWithoutGapsOrDuplicates()
    {
        var store = CreateStore();
        const string segment = "orders";
        const int total = 120;
        const int appendBatch = 20;
        for (var start = 1; start <= total; start += appendBatch)
        {
            var batch = Enumerable.Range(start, appendBatch).Select(index => "record-" + index).ToArray();
            await AppendMarkersAsync(store, segment, batch).ConfigureAwait(false);
        }

        const int pageSize = 25;
        List<long> sequences = [];
        for (var from = 1; from <= total; from += pageSize)
        {
            var page = await CollectAsync(
                store.ReadSegmentAsync(segment, from, Math.Min(from + pageSize - 1, total), CancellationToken.None),
                CancellationToken.None).ConfigureAwait(false);
            foreach (var document in page)
            {
                sequences.Add(document.Sequence);
            }
        }

        sequences.Should().HaveCount(total);
        for (var index = 0; index < sequences.Count; index++)
        {
            sequences[index].Should().Be(index + 1);
        }

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.LastSequence.Should().Be(total);
        head.Count.Should().Be(total);
    }

    /// <summary>
    ///     Verifies that a tenant head filter returns only that tenant's segments while a null tenant returns every
    ///     segment.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListHeadsAsync_WhenTenantFilterIsSet_ShouldReturnOnlyThatTenant()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "tenant-a-orders", ["a-1"], tenantId: "tenant-a").ConfigureAwait(false);
        await AppendMarkersAsync(store, "tenant-a-payments", ["a-2"], tenantId: "tenant-a").ConfigureAwait(false);
        await AppendMarkersAsync(store, "tenant-b-orders", ["b-1"], tenantId: "tenant-b").ConfigureAwait(false);
        await AppendMarkersAsync(store, "unscoped", ["u-1"]).ConfigureAwait(false);

        var tenantA = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(TenantId: "tenant-a"), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        tenantA.Select(head => head.Info.Key).Should().BeEquivalentTo(["tenant-a-orders", "tenant-a-payments"]);

        var all = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        all.Select(head => head.Info.Key).Should()
            .BeEquivalentTo(["tenant-a-orders", "tenant-a-payments", "tenant-b-orders", "unscoped"]);
    }

    /// <summary>
    ///     Verifies that an attribute head filter matches only segments carrying those values, while a null or empty
    ///     filter constrains nothing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListHeadsAsync_WhenAttributesAreSet_ShouldMatchOnlySegmentsCarryingThem()
    {
        var store = CreateStore();
        var tax = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "tax" };
        var operations = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "ops" };
        await AppendMarkersAsync(store, "tax", ["t-1"], attributes: tax).ConfigureAwait(false);
        await AppendMarkersAsync(store, "operations", ["o-1"], attributes: operations).ConfigureAwait(false);
        await AppendMarkersAsync(store, "plain", ["p-1"]).ConfigureAwait(false);

        var filter = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "tax" };
        var matching = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(Attributes: filter), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        matching.Select(head => head.Info.Key).Should().BeEquivalentTo(["tax"]);

        var unconstrained = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unconstrained.Should().HaveCount(3);

        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var emptyFilter = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(Attributes: empty), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        emptyFilter.Should().HaveCount(3);
    }

    /// <summary>
    ///     Verifies that an opened-before head filter returns only segments opened before that instant.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListHeadsAsync_WhenOpenedBeforeIsSet_ShouldReturnOnlyOlderSegments()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "older", ["o-1"], openedAt: BaseTime).ConfigureAwait(false);
        await AppendMarkersAsync(store, "newer", ["n-1"], openedAt: BaseTime.AddHours(2)).ConfigureAwait(false);

        var older = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(OpenedBefore: BaseTime.AddHours(1)), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        older.Select(head => head.Info.Key).Should().BeEquivalentTo(["older"]);

        var all = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        all.Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies that head listing excludes expired segments unless the filter includes them.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListHeadsAsync_WhenIncludeExpiredIsFalse_ShouldExcludeExpiredSegments()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "open", ["o-1"]).ConfigureAwait(false);
        await AppendMarkersAsync(store, "closed", ["c-1"], expired: true).ConfigureAwait(false);

        var open = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(IncludeExpired: false), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        open.Select(head => head.Info.Key).Should().BeEquivalentTo(["open"]);

        var byDefault = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        byDefault.Select(head => head.Info.Key).Should().BeEquivalentTo(["open"]);

        var all = await CollectAsync(
            store.ListHeadsAsync(new AuditSegmentFilter(IncludeExpired: true), CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        all.Select(head => head.Info.Key).Should().BeEquivalentTo(["open", "closed"]);
    }

    /// <summary>
    ///     Verifies that a stored checkpoint reads back with its label, signatures, and publish state, and that an
    ///     unknown checkpoint id reads back as null.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task StoreCheckpointAsync_ShouldRoundTripCheckpoint()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1", "record-2"]).ConfigureAwait(false);

        var checkpoint = CreateCheckpoint(segment, sequence: 2, count: 2, hash: HashFor(2)) with
        {
            Label = "hourly",
            Signature = new AuditCheckpointSignature("hmac-sha256", "signature-1"),
            Signatures =
            [
                new AuditCheckpointSignature("hmac-sha256", "signature-1"),
                new AuditCheckpointSignature("rfc3161", "token-1"),
            ],
        };

        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        stored.Should().NotBeNull();
        AssertCheckpointMatches(stored!, checkpoint);

        var unknown = await store.ReadCheckpointAsync(Guid.NewGuid(), CancellationToken.None).ConfigureAwait(false);
        unknown.Should().BeNull();
    }

    /// <summary>
    ///     Verifies that a final checkpoint keeps its kind, so expiry can leave it behind as proof of what was
    ///     removed.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task StoreCheckpointAsync_WhenKindIsFinal_ShouldPreserveKind()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        var checkpoint = CreateCheckpoint(
            segment,
            sequence: 1,
            count: 1,
            hash: HashFor(1),
            kind: AuditCheckpointKind.Final);
        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Kind.Should().Be(AuditCheckpointKind.Final);
        stored.Id.Should().Be(checkpoint.Id);
    }

    /// <summary>
    ///     Verifies that the unpublished listing holds only checkpoints still waiting for publication.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ListUnpublishedAsync_ShouldListOnlyPendingCheckpoints()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        var pending = CreateCheckpoint(segment, sequence: 1, count: 1, hash: HashFor(1));
        var published = CreateCheckpoint(segment, sequence: 1, count: 1, hash: HashFor(1));
        await store.StoreCheckpointAsync(pending, CancellationToken.None).ConfigureAwait(false);
        await store.StoreCheckpointAsync(published, CancellationToken.None).ConfigureAwait(false);
        await store.MarkPublishedAsync(published.Id, null, CancellationToken.None).ConfigureAwait(false);

        var unpublished = await CollectAsync(
            store.ListUnpublishedAsync(CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);

        unpublished.Select(checkpoint => checkpoint.Id).Should().BeEquivalentTo([pending.Id]);
    }

    /// <summary>
    ///     Verifies that a successful publish marks the checkpoint published with its identity unchanged and removes
    ///     it from the unpublished listing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task MarkPublishedAsync_WhenPublishSucceeds_ShouldMarkPublishedWithStableIdentity()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        var checkpoint = CreateCheckpoint(segment, sequence: 1, count: 1, hash: HashFor(1)) with { Label = "hourly" };
        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        await store.MarkPublishedAsync(checkpoint.Id, null, CancellationToken.None).ConfigureAwait(false);

        var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.PublishState.Should().Be(CheckpointPublishState.Published);
        stored.Id.Should().Be(checkpoint.Id);
        stored.Segment.Should().Be(checkpoint.Segment);
        stored.Sequence.Should().Be(checkpoint.Sequence);
        stored.Hash.Should().Be(checkpoint.Hash);
        stored.Label.Should().Be("hourly");

        var unpublished = await CollectAsync(
            store.ListUnpublishedAsync(CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unpublished.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that a failed publish keeps the checkpoint pending with the attempt recorded, and that a later
    ///     success publishes the same checkpoint identity.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task MarkPublishedAsync_WhenPublishFails_ShouldStayPendingWithAttemptRecorded()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        var checkpoint = CreateCheckpoint(segment, sequence: 1, count: 1, hash: HashFor(1));
        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        await store.MarkPublishedAsync(checkpoint.Id, "anchor unreachable", CancellationToken.None).ConfigureAwait(false);

        var afterFirstFailure = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        afterFirstFailure.Should().NotBeNull();
        afterFirstFailure!.PublishState.Should().Be(CheckpointPublishState.Pending);
        afterFirstFailure.PublishAttempts.Should().Be(1);
        afterFirstFailure.LastPublishError.Should().Be("anchor unreachable");
        afterFirstFailure.Id.Should().Be(checkpoint.Id);

        await store.MarkPublishedAsync(checkpoint.Id, "anchor timed out", CancellationToken.None).ConfigureAwait(false);

        var afterSecondFailure = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        afterSecondFailure.Should().NotBeNull();
        afterSecondFailure!.PublishState.Should().Be(CheckpointPublishState.Pending);
        afterSecondFailure.PublishAttempts.Should().Be(2);
        afterSecondFailure.LastPublishError.Should().Be("anchor timed out");

        var unpublished = await CollectAsync(
            store.ListUnpublishedAsync(CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unpublished.Select(candidate => candidate.Id).Should().BeEquivalentTo([checkpoint.Id]);

        await store.MarkPublishedAsync(checkpoint.Id, null, CancellationToken.None).ConfigureAwait(false);

        var published = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        published.Should().NotBeNull();
        published!.PublishState.Should().Be(CheckpointPublishState.Published);
        published.Id.Should().Be(checkpoint.Id);

        var afterPublish = await CollectAsync(
            store.ListUnpublishedAsync(CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        afterPublish.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that erasing one tenant removes its records, staged documents, heads, and stored checkpoints while
    ///     another tenant's segment stays complete and still pending publication.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task EraseAsync_WhenTenantFilterIsSet_ShouldRemoveOnlyThatTenant()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "tenant-a-orders", ["a-1", "a-2"], tenantId: "tenant-a").ConfigureAwait(false);
        await store.StageAsync("tenant-a-orders", CreateDocument("a-staged"), CancellationToken.None).ConfigureAwait(false);
        var checkpointA = CreateCheckpoint("tenant-a-orders", sequence: 2, count: 2, hash: HashFor(2));
        await store.StoreCheckpointAsync(checkpointA, CancellationToken.None).ConfigureAwait(false);

        await AppendMarkersAsync(store, "tenant-b-orders", ["b-1", "b-2"], tenantId: "tenant-b").ConfigureAwait(false);
        await store.StageAsync("tenant-b-orders", CreateDocument("b-staged"), CancellationToken.None).ConfigureAwait(false);
        var checkpointB = CreateCheckpoint("tenant-b-orders", sequence: 2, count: 2, hash: HashFor(2));
        await store.StoreCheckpointAsync(checkpointB, CancellationToken.None).ConfigureAwait(false);

        await store.EraseAsync(new AuditSegmentFilter(TenantId: "tenant-a"), CancellationToken.None).ConfigureAwait(false);

        var erasedHead = await store.ReadHeadAsync("tenant-a-orders", CancellationToken.None).ConfigureAwait(false);
        erasedHead.Should().BeNull();

        var erasedDocuments = await CollectAsync(
            store.ReadSegmentAsync("tenant-a-orders", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        erasedDocuments.Should().BeEmpty();

        var erasedUnlinked = await CollectAsync(
            store.ListUnlinkedAsync("tenant-a-orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        erasedUnlinked.Should().BeEmpty();

        var erasedCheckpoint = await store.ReadCheckpointAsync(checkpointA.Id, CancellationToken.None).ConfigureAwait(false);
        erasedCheckpoint.Should().BeNull();

        var keptHead = await store.ReadHeadAsync("tenant-b-orders", CancellationToken.None).ConfigureAwait(false);
        keptHead.Should().NotBeNull();
        keptHead!.Count.Should().Be(2);
        keptHead.LastSequence.Should().Be(2);

        var keptDocuments = await CollectAsync(
            store.ReadSegmentAsync("tenant-b-orders", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        keptDocuments.Should().HaveCount(2);
        GetMarker(keptDocuments[0]).Should().Be("b-1");
        GetMarker(keptDocuments[1]).Should().Be("b-2");

        var keptUnlinked = await CollectAsync(
            store.ListUnlinkedAsync("tenant-b-orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        keptUnlinked.Should().ContainSingle();
        GetMarker(keptUnlinked[0]).Should().Be("b-staged");

        var keptCheckpoint = await store.ReadCheckpointAsync(checkpointB.Id, CancellationToken.None).ConfigureAwait(false);
        keptCheckpoint.Should().NotBeNull();
        keptCheckpoint!.PublishState.Should().Be(CheckpointPublishState.Pending);

        var unpublished = await CollectAsync(
            store.ListUnpublishedAsync(CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unpublished.Select(checkpoint => checkpoint.Id).Should().BeEquivalentTo([checkpointB.Id]);
    }

    /// <summary>
    ///     Verifies that erasing by attributes removes only the matching segments.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task EraseAsync_WhenFilterSelectsByAttributes_ShouldRemoveOnlyMatchingSegments()
    {
        var store = CreateStore();
        var tax = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "tax" };
        var operations = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "ops" };
        await AppendMarkersAsync(store, "tax", ["t-1"], attributes: tax).ConfigureAwait(false);
        await AppendMarkersAsync(store, "operations", ["o-1"], attributes: operations).ConfigureAwait(false);

        var filter = new Dictionary<string, string>(StringComparer.Ordinal) { ["class"] = "tax" };
        await store.EraseAsync(new AuditSegmentFilter(Attributes: filter), CancellationToken.None).ConfigureAwait(false);

        var erasedHead = await store.ReadHeadAsync("tax", CancellationToken.None).ConfigureAwait(false);
        erasedHead.Should().BeNull();

        var erasedDocuments = await CollectAsync(
            store.ReadSegmentAsync("tax", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        erasedDocuments.Should().BeEmpty();

        var keptHead = await store.ReadHeadAsync("operations", CancellationToken.None).ConfigureAwait(false);
        keptHead.Should().NotBeNull();

        var keptDocuments = await CollectAsync(
            store.ReadSegmentAsync("operations", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        keptDocuments.Should().ContainSingle();
        GetMarker(keptDocuments[0]).Should().Be("o-1");
    }

    /// <summary>
    ///     Verifies that erasing with a filter that matches nothing changes nothing.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task EraseAsync_WhenFilterMatchesNothing_ShouldChangeNothing()
    {
        var store = CreateStore();
        await AppendMarkersAsync(store, "orders", ["record-1"], tenantId: "tenant-a").ConfigureAwait(false);
        var checkpoint = CreateCheckpoint("orders", sequence: 1, count: 1, hash: HashFor(1));
        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        await store.EraseAsync(new AuditSegmentFilter(TenantId: "tenant-nobody"), CancellationToken.None).ConfigureAwait(false);

        var head = await store.ReadHeadAsync("orders", CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.Count.Should().Be(1);

        var documents = await CollectAsync(
            store.ReadSegmentAsync("orders", 1, null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        documents.Should().ContainSingle();

        var stored = await store.ReadCheckpointAsync(checkpoint.Id, CancellationToken.None).ConfigureAwait(false);
        stored.Should().NotBeNull();
    }

    /// <summary>
    ///     Verifies that a head read honors cancellation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ReadHeadAsync_WhenCancellationIsRequested_ShouldThrow()
    {
        var store = CreateStore();
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var read = async () =>
        {
            await store.ReadHeadAsync("orders", cancellationSource.Token).ConfigureAwait(false);
        };

        await read.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that cancellation requested before an append prevents any store mutation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task AppendAsync_WhenCancellationIsRequested_ShouldNotStoreDocuments()
    {
        var store = CreateStore();
        const string segment = "orders";
        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var expected = CreateHead(segment, version: 0, lastSequence: 0, lastHash: null, count: 0);
        var document = CreateLinkedDocument(segment, "record-1", 1, null, HashFor(1));
        var updated = CreateHead(segment, version: 1, lastSequence: 1, lastHash: HashFor(1), count: 1);

        var append = async () =>
        {
            await store.AppendAsync(segment, expected, [document], updated, cancellationSource.Token).ConfigureAwait(false);
        };

        await append.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().BeNull();
    }

    /// <summary>
    ///     Verifies that a segment read honors cancellation while streaming.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public async Task ReadSegmentAsync_WhenCancellationIsRequested_ShouldThrow()
    {
        var store = CreateStore();
        const string segment = "orders";
        await AppendMarkersAsync(store, segment, ["record-1"]).ConfigureAwait(false);

        using var cancellationSource = new CancellationTokenSource();
        await cancellationSource.CancelAsync().ConfigureAwait(false);

        var read = async () =>
        {
            await CollectAsync(
                store.ReadSegmentAsync(segment, 1, null, cancellationSource.Token),
                cancellationSource.Token).ConfigureAwait(false);
        };

        await read.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds a linked document carrying the given marker and chain position.
    /// </summary>
    /// <param name="segment">The segment the document is chained in.</param>
    /// <param name="marker">The opaque marker stored in the document payload.</param>
    /// <param name="sequence">The position of the document in its segment, starting at one.</param>
    /// <param name="previousHash">The hash of the previous document, or <see langword="null" /> for the first one.</param>
    /// <param name="hash">The hash of this document.</param>
    /// <returns>A document with its chain fields set.</returns>
    private TDocument CreateLinkedDocument(string segment, string marker, long sequence, string? previousHash, string hash)
    {
        var document = CreateDocument(marker);
        document.Segment = segment;
        document.Sequence = sequence;
        document.PreviousHash = previousHash;
        document.Hash = hash;
        document.HashScheme = AuditHashChainOptions.DefaultSchemeName;

        return document;
    }

    /// <summary>
    ///     Appends markers to a segment, continuing its sequences from the stored head the way a linker does.
    /// </summary>
    /// <param name="store">The store under test.</param>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="markers">The markers to append, in ascending sequence order.</param>
    /// <param name="tenantId">The tenant stored on a new head, or <see langword="null" /> for no tenant.</param>
    /// <param name="attributes">The attributes stored on a new head, or <see langword="null" /> for none.</param>
    /// <param name="openedAt">When a new head was opened, or <see langword="null" /> for the baseline time.</param>
    /// <param name="expired">Whether a new head is expired and closed to new writes.</param>
    /// <returns>The head after the append.</returns>
    private async Task<AuditSegmentHead> AppendMarkersAsync(
        IAuditChainStore<TDocument> store,
        string segment,
        IReadOnlyList<string> markers,
        string? tenantId = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        DateTimeOffset? openedAt = null,
        bool expired = false)
    {
        var stored = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        var expected = stored ?? CreateHead(segment, 0, 0, null, 0, tenantId, attributes, openedAt, expired);
        var documents = new TDocument[markers.Count];
        for (var index = 0; index < markers.Count; index++)
        {
            var sequence = expected.LastSequence + index + 1;
            string? previousHash;
            if (sequence == 1)
            {
                previousHash = null;
            }
            else if (index == 0)
            {
                previousHash = expected.LastHash;
            }
            else
            {
                previousHash = HashFor(sequence - 1);
            }

            documents[index] = CreateLinkedDocument(segment, markers[index], sequence, previousHash, HashFor(sequence));
        }

        var updated = expected with
        {
            LastSequence = expected.LastSequence + markers.Count,
            LastHash = HashFor(expected.LastSequence + markers.Count),
            Count = expected.Count + markers.Count,
            Version = expected.Version + 1,
        };

        await store.AppendAsync(segment, expected, documents, updated, CancellationToken.None).ConfigureAwait(false);

        return updated;
    }

    /// <summary>
    ///     Attempts one conditional append, reporting a lost race as <see langword="false" /> instead of throwing.
    /// </summary>
    /// <param name="store">The store under test.</param>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="expected">The head version the append asserts.</param>
    /// <param name="document">The linked document to append.</param>
    /// <param name="updated">The head after the append.</param>
    /// <returns>
    ///     <see langword="true" /> when the append won the race; <see langword="false" /> when it lost with an
    ///     <see cref="AuditHeadConflictException" />.
    /// </returns>
    private static async Task<bool> TryAppendAsync(
        IAuditChainStore<TDocument> store,
        string segment,
        AuditSegmentHead expected,
        TDocument document,
        AuditSegmentHead updated)
    {
        try
        {
            await store.AppendAsync(segment, expected, [document], updated, CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (AuditHeadConflictException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Builds a segment head for contract tests.
    /// </summary>
    /// <param name="segment">The segment key.</param>
    /// <param name="version">The head version the store checks on a conditional write.</param>
    /// <param name="lastSequence">The sequence of the last linked document, or zero when nothing is linked.</param>
    /// <param name="lastHash">The hash of the last linked document, or <see langword="null" /> when nothing is linked.</param>
    /// <param name="count">The number of linked documents in the segment.</param>
    /// <param name="tenantId">The tenant the segment belongs to, or <see langword="null" /> when unscoped.</param>
    /// <param name="attributes">The attributes stored on the head, or <see langword="null" /> for none.</param>
    /// <param name="openedAt">When the segment was opened, or <see langword="null" /> for the baseline time.</param>
    /// <param name="expired">Whether the segment expired and is closed to new writes.</param>
    /// <returns>The head.</returns>
    private AuditSegmentHead CreateHead(
        string segment,
        long version,
        long lastSequence,
        string? lastHash,
        long count,
        string? tenantId = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        DateTimeOffset? openedAt = null,
        bool expired = false)
    {
        var info = new AuditSegmentInfo(
            segment,
            tenantId,
            attributes ?? new Dictionary<string, string>(StringComparer.Ordinal),
            openedAt ?? BaseTime);

        return new AuditSegmentHead(info, lastSequence, lastHash, count, null, null, expired, version);
    }

    /// <summary>
    ///     Builds a stored checkpoint waiting for publication.
    /// </summary>
    /// <param name="segment">The segment the checkpoint covers.</param>
    /// <param name="sequence">The sequence of the last document the checkpoint covers.</param>
    /// <param name="count">The number of documents in the segment at the checkpoint.</param>
    /// <param name="hash">The hash of the document at <paramref name="sequence" />.</param>
    /// <param name="kind">Why the checkpoint was cut.</param>
    /// <returns>The checkpoint with a new identifier.</returns>
    private AuditCheckpoint CreateCheckpoint(
        string segment,
        long sequence,
        long count,
        string hash,
        AuditCheckpointKind kind = AuditCheckpointKind.Regular)
    {
        return new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            sequence,
            count,
            hash,
            AuditHashChainOptions.DefaultSchemeName,
            kind,
            BaseTime)
        {
            PublishState = CheckpointPublishState.Pending,
        };
    }

    /// <summary>
    ///     Builds a distinct opaque hash value for a sequence.
    /// </summary>
    /// <param name="sequence">The sequence the hash stands for.</param>
    /// <returns>An opaque hash value.</returns>
    private static string HashFor(long sequence)
    {
        return "hash-" + sequence;
    }

    /// <summary>
    ///     Drains an asynchronous stream into a list.
    /// </summary>
    /// <typeparam name="T">The streamed item type.</typeparam>
    /// <param name="source">The stream to drain.</param>
    /// <param name="cancellationToken">The token used to cancel the enumeration.</param>
    /// <returns>The streamed items in stream order.</returns>
    private static async Task<IReadOnlyList<T>> CollectAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        List<T> items = [];
        var enumerator = source.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                items.Add(enumerator.Current);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        return items;
    }

    /// <summary>
    ///     Asserts that a stored head matches the expected head field by field.
    /// </summary>
    /// <param name="actual">The head the store returned.</param>
    /// <param name="expected">The head the test appended.</param>
    private static void AssertHeadMatches(AuditSegmentHead actual, AuditSegmentHead expected)
    {
        actual.Info.Key.Should().Be(expected.Info.Key);
        actual.Info.TenantId.Should().Be(expected.Info.TenantId);
        actual.Info.Attributes.Should().BeEquivalentTo(expected.Info.Attributes);
        actual.Info.OpenedAt.Should().Be(expected.Info.OpenedAt);
        actual.LastSequence.Should().Be(expected.LastSequence);
        actual.LastHash.Should().Be(expected.LastHash);
        actual.Count.Should().Be(expected.Count);
        actual.LastCheckpointSequence.Should().Be(expected.LastCheckpointSequence);
        actual.LastCheckpointAt.Should().Be(expected.LastCheckpointAt);
        actual.Expired.Should().Be(expected.Expired);
        actual.Version.Should().Be(expected.Version);
    }

    /// <summary>
    ///     Asserts that a stored checkpoint matches the expected checkpoint field by field.
    /// </summary>
    /// <param name="actual">The checkpoint the store returned.</param>
    /// <param name="expected">The checkpoint the test stored.</param>
    private static void AssertCheckpointMatches(AuditCheckpoint actual, AuditCheckpoint expected)
    {
        actual.Id.Should().Be(expected.Id);
        actual.Segment.Should().Be(expected.Segment);
        actual.Sequence.Should().Be(expected.Sequence);
        actual.Count.Should().Be(expected.Count);
        actual.Hash.Should().Be(expected.Hash);
        actual.HashScheme.Should().Be(expected.HashScheme);
        actual.Kind.Should().Be(expected.Kind);
        actual.CreatedAt.Should().Be(expected.CreatedAt);
        actual.Signature.Should().Be(expected.Signature);
        actual.Label.Should().Be(expected.Label);
        actual.PublishState.Should().Be(expected.PublishState);
        actual.PublishAttempts.Should().Be(expected.PublishAttempts);
        actual.LastPublishError.Should().Be(expected.LastPublishError);
        actual.Signatures.Should().BeEquivalentTo(expected.Signatures);
    }
}
