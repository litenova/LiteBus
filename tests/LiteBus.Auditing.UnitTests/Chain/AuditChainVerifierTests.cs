using LiteBus.Auditing.Hashing;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Verification;

namespace LiteBus.Auditing.UnitTests.Chain;

/// <summary>
///     Verifies that the storage-free audit chain verifier reports every break in a segment.
/// </summary>
public sealed class AuditChainVerifierTests
{
    /// <summary>
    ///     The segment every chain in these tests is appended to.
    /// </summary>
    private const string Segment = "orders/2026";

    [Fact]
    public async Task An_intact_chain_verifies_with_no_breaks()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.Breaks.Should().BeEmpty();
        verification.Segment.Should().Be(Segment);
        verification.RecordCount.Should().Be(3);
        verification.HeadSequence.Should().Be(3);
        verification.HeadHash.Should().Be(documents[2].Hash);
        verification.LatestCheckpoint.Should().BeNull();
        verification.MatchesLatestCheckpoint.Should().BeNull();
        verification.CheckpointSignatureValid.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_segment_verifies_intact()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, []).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(0);
        verification.HeadSequence.Should().BeNull();
        verification.HeadHash.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_sequence_is_reported_as_a_gap()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents.RemoveAt(1);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // The document after the gap still points at the removed hash, so its positional link breaks beside the gap.
        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Gap),
                (3L, AuditChainBreakKind.PreviousHashMismatch)
            ]);
    }

    [Fact]
    public async Task A_repeated_sequence_is_reported_as_a_duplicate()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var duplicate = new ChainTestDocument
        {
            Segment = Segment,
            Sequence = 2,
            PreviousHash = documents[0].Hash,
            HashScheme = scheme.Name,
            Action = "orders.step-2-again"
        };
        await AuditChainTestData.RehashAsync(duplicate, scheme).ConfigureAwait(false);
        documents.Add(duplicate);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // Both documents claim sequence two, so the second one also follows the wrong hash positionally.
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Duplicate),
                (2L, AuditChainBreakKind.PreviousHashMismatch)
            ]);
    }

    [Fact]
    public async Task A_misplaced_sequence_is_reported_as_a_duplicate()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var built = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);

        // Reorder to one, three, two, then relink positionally so only the ordering breaks.
        List<ChainTestDocument> documents = [built[0], built[2], built[1]];
        documents[1].PreviousHash = documents[0].Hash;
        await AuditChainTestData.RehashAsync(documents[1], scheme).ConfigureAwait(false);
        documents[2].PreviousHash = documents[1].Hash;
        await AuditChainTestData.RehashAsync(documents[2], scheme).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.Duplicate);
    }

    [Fact]
    public async Task A_tampered_document_is_reported_as_a_hash_mismatch()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[1].Action = "orders.refund-order";
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // The stored hash and links are untouched, so only the recomputation of the altered document breaks.
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.HashMismatch);
        single.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_rewritten_previous_hash_is_reported()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[2].PreviousHash = documents[0].Hash;
        await AuditChainTestData.RehashAsync(documents[2], scheme).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // The hash is recomputed after the rewrite, so only the predecessor link breaks.
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.PreviousHashMismatch);
    }

    [Fact]
    public async Task An_unknown_scheme_is_reported()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[2].HashScheme = "retired-v0";
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.UnknownHashScheme);
    }

    [Fact]
    public async Task A_document_naming_no_scheme_is_reported()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        documents[1].HashScheme = null;
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.UnknownHashScheme);
    }

    [Fact]
    public async Task Two_schemes_in_one_segment_verify()
    {
        var first = AuditChainTestData.TestScheme("sha256-v1");
        byte[] key = [1, 2, 3, 4];
        var second = AuditChainTestData.TestScheme(
            "hmac-sha256-v1", new HmacSha256AuditHasher(key));
        var documents = await AuditChainTestData
            .BuildChainAsync(Segment, [first, first, second, second])
            .ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([first, second]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(4);
        verification.HeadSequence.Should().Be(4);
    }

    [Fact]
    public async Task A_chain_missing_one_of_its_schemes_reports_unknown_scheme()
    {
        var first = AuditChainTestData.TestScheme("sha256-v1");
        var second = AuditChainTestData.TestScheme("hmac-sha256-v1");
        var documents = await AuditChainTestData
            .BuildChainAsync(Segment, [first, first, second, second])
            .ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([first]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (3L, AuditChainBreakKind.UnknownHashScheme),
                (4L, AuditChainBreakKind.UnknownHashScheme)
            ]);
    }

    [Fact]
    public async Task A_matching_checkpoint_compares_equal()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.LatestCheckpoint.Should().Be(checkpoint);
        verification.MatchesLatestCheckpoint.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeNull();
    }

    [Fact]
    public async Task A_rewritten_checkpoint_hash_is_reported()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents) with { Hash = "bogus" };
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointHashMismatch);
        verification.MatchesLatestCheckpoint.Should().BeFalse();
    }

    [Fact]
    public async Task A_checkpoint_count_above_the_segment_is_reported()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents) with { Count = 5 };
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointCountMismatch);
        verification.MatchesLatestCheckpoint.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_covered_document_breaks_the_checkpoint_count()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents);
        documents.RemoveAt(1);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Gap),
                (3L, AuditChainBreakKind.PreviousHashMismatch),
                (3L, AuditChainBreakKind.CheckpointCountMismatch)
            ]);
    }

    [Fact]
    public async Task A_checkpoint_for_a_missing_sequence_breaks_the_checkpoint_hash()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents) with { Sequence = 5 };
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Should().Contain(breakItem =>
            breakItem.Sequence == 5 && breakItem.Kind == AuditChainBreakKind.CheckpointHashMismatch);
    }

    [Fact]
    public async Task A_signed_checkpoint_verifies()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var signer = new ChainTestSigner([7, 8, 9, 10]);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents);
        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme], signer);

        var verification = await verifier
            .VerifyAsync(Segment, documents, checkpoint with { Signature = signature })
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.MatchesLatestCheckpoint.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_tampered_signed_checkpoint_is_reported()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var signer = new ChainTestSigner([7, 8, 9, 10]);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents);
        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var tampered = checkpoint with { Hash = "bogus", Signature = signature };
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme], signer);

        var verification = await verifier.VerifyAsync(Segment, documents, tampered).ConfigureAwait(false);

        verification.CheckpointSignatureValid.Should().BeFalse();
        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Select(breakItem => breakItem.Kind)
            .Should().BeEquivalentTo(
            [
                AuditChainBreakKind.CheckpointHashMismatch,
                AuditChainBreakKind.CheckpointSignatureInvalid
            ]);
    }

    [Fact]
    public async Task An_unsigned_checkpoint_with_a_signer_is_reported_as_an_invalid_signature()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<ChainTestDocument>(
            [scheme], new ChainTestSigner([7, 8, 9, 10]));

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointSignatureInvalid);
        verification.CheckpointSignatureValid.Should().BeFalse();

        // The chain still covers what the checkpoint counted; only the signature is missing.
        verification.MatchesLatestCheckpoint.Should().BeTrue();
    }

    [Fact]
    public async Task Without_a_signer_an_unsigned_checkpoint_leaves_validity_unknown()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeNull();
    }

    [Fact]
    public async Task Every_break_is_reported_not_only_the_first()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        documents.RemoveAt(1);
        documents[2].Action = "orders.refund-order";
        documents[3].HashScheme = "retired-v0";
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, documents) with { Count = 9 };
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Select(breakItem => breakItem.Kind).Should().Contain(
        [
            AuditChainBreakKind.Gap,
            AuditChainBreakKind.HashMismatch,
            AuditChainBreakKind.UnknownHashScheme,
            AuditChainBreakKind.CheckpointCountMismatch
        ]);
        verification.Breaks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task An_empty_segment_misses_its_checkpoint()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var full = await AuditChainTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var checkpoint = AuditChainTestData.CheckpointFor(Segment, full);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, [], checkpoint).ConfigureAwait(false);

        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Select(breakItem => breakItem.Kind)
            .Should().BeEquivalentTo(
            [
                AuditChainBreakKind.CheckpointHashMismatch,
                AuditChainBreakKind.CheckpointCountMismatch
            ]);
    }

    [Fact]
    public void A_lookup_with_a_null_scheme_is_rejected()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var lookup = new Dictionary<string, AuditHashScheme<ChainTestDocument>>
        {
            [scheme.Name] = null!
        };

        var act = () => new AuditChainVerifier<ChainTestDocument>(lookup);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Schemes_with_a_repeated_name_are_rejected()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");

        var act = () => new AuditChainVerifier<ChainTestDocument>([scheme, scheme]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_blank_segment_is_rejected()
    {
        var scheme = AuditChainTestData.TestScheme("sha256-v1");
        var documents = await AuditChainTestData.BuildChainAsync(Segment, scheme, 1).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<ChainTestDocument>([scheme]);

        var act = () => verifier.VerifyAsync("  ", documents);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }
}
