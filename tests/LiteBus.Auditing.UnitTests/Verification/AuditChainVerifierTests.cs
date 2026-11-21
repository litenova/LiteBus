using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Verification;
using LiteBus.Auditing.Recording;

namespace LiteBus.Auditing.UnitTests.Verification;

/// <summary>
///     Verifies that the storage-free audit chain verifier reports every break in a segment, by list and by pages.
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
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

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
    public async Task An_intact_chain_verifies_by_pages_with_no_breaks()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, ToPages(documents)).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.Breaks.Should().BeEmpty();
        verification.RecordCount.Should().Be(5);
        verification.HeadSequence.Should().Be(5);
        verification.HeadHash.Should().Be(documents[4].Hash);
    }

    [Fact]
    public async Task An_empty_segment_verifies_intact()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, []).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(0);
        verification.HeadSequence.Should().BeNull();
        verification.HeadHash.Should().BeNull();
    }

    [Fact]
    public async Task An_empty_segment_verifies_by_pages_intact()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier
            .VerifyAsync(Segment, ToPages<VerificationTestDocument>([]))
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(0);
        verification.HeadSequence.Should().BeNull();
        verification.HeadHash.Should().BeNull();
    }

    [Fact]
    public async Task A_missing_sequence_is_reported_as_a_gap()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents.RemoveAt(1);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // The document after the gap still points at the removed hash, so its positional link breaks beside the gap.
        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Gap),
                (3L, AuditChainBreakKind.PreviousHashMismatch),
            ]);
    }

    [Fact]
    public async Task A_missing_sequence_is_reported_by_pages_as_a_gap()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents.RemoveAt(1);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, ToPages(documents)).ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Gap),
                (3L, AuditChainBreakKind.PreviousHashMismatch),
            ]);
    }

    [Fact]
    public async Task A_repeated_sequence_is_reported_as_a_duplicate()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var duplicate = new VerificationTestDocument
        {
            Segment = Segment,
            Sequence = 2,
            PreviousHash = documents[0].Hash,
            HashScheme = scheme.Name,
            Action = "orders.step-2-again",
        };
        await VerificationTestData.RehashAsync(duplicate, scheme).ConfigureAwait(false);
        documents.Add(duplicate);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // Both documents claim sequence two, so the second one also follows the wrong hash positionally.
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Duplicate),
                (2L, AuditChainBreakKind.PreviousHashMismatch),
            ]);
    }

    [Fact]
    public async Task A_misplaced_sequence_is_reported_as_a_duplicate()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var built = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);

        // Reorder to one, three, two, then relink positionally so only the ordering breaks.
        List<VerificationTestDocument> documents = [built[0], built[2], built[1]];
        documents[1].PreviousHash = documents[0].Hash;
        await VerificationTestData.RehashAsync(documents[1], scheme).ConfigureAwait(false);
        documents[2].PreviousHash = documents[1].Hash;
        await VerificationTestData.RehashAsync(documents[2], scheme).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.Duplicate);
    }

    [Fact]
    public async Task A_tampered_document_is_reported_as_a_hash_mismatch()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[1].Action = "orders.refund-order";
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // The stored hash and links are untouched, so only the recomputation of the altered document breaks.
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.HashMismatch);
        single.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_tampered_document_is_reported_by_pages_as_a_hash_mismatch()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[1].Action = "orders.refund-order";
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, ToPages(documents)).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.HashMismatch);
    }

    [Fact]
    public async Task A_rewritten_previous_hash_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[2].PreviousHash = documents[0].Hash;
        await VerificationTestData.RehashAsync(documents[2], scheme).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        // The hash is recomputed after the rewrite, so only the predecessor link breaks.
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.PreviousHashMismatch);
    }

    [Fact]
    public async Task An_unknown_scheme_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[2].HashScheme = "retired-v0";
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.UnknownHashScheme);
    }

    [Fact]
    public async Task A_document_naming_no_scheme_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        documents[1].HashScheme = null;
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(2);
        single.Kind.Should().Be(AuditChainBreakKind.UnknownHashScheme);
    }

    [Fact]
    public async Task Two_schemes_in_one_segment_verify()
    {
        var first = VerificationTestData.TestScheme("sha256-v1");
        byte[] key = [1, 2, 3, 4];
        var second = VerificationTestData.TestScheme(
            "hmac-sha256-v1", new HmacSha256AuditHasher(key));
        var documents = await VerificationTestData
            .BuildChainAsync(Segment, [first, first, second, second])
            .ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([first, second]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(4);
        verification.HeadSequence.Should().Be(4);
    }

    [Fact]
    public async Task A_chain_missing_one_of_its_schemes_reports_unknown_scheme()
    {
        var first = VerificationTestData.TestScheme("sha256-v1");
        var second = VerificationTestData.TestScheme("hmac-sha256-v1");
        var documents = await VerificationTestData
            .BuildChainAsync(Segment, [first, first, second, second])
            .ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([first]);

        var verification = await verifier.VerifyAsync(Segment, documents).ConfigureAwait(false);

        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (3L, AuditChainBreakKind.UnknownHashScheme),
                (4L, AuditChainBreakKind.UnknownHashScheme),
            ]);
    }

    [Fact]
    public async Task A_matching_checkpoint_compares_equal()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.LatestCheckpoint.Should().Be(checkpoint);
        verification.MatchesLatestCheckpoint.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeNull();
    }

    [Fact]
    public async Task A_matching_checkpoint_compares_equal_by_pages()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, ToPages(documents), checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.MatchesLatestCheckpoint.Should().BeTrue();
    }

    [Fact]
    public async Task A_rewritten_checkpoint_hash_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with { Hash = "bogus" };
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointHashMismatch);
        verification.MatchesLatestCheckpoint.Should().BeFalse();
    }

    [Fact]
    public async Task A_rewritten_checkpoint_hash_is_reported_by_pages()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with { Hash = "bogus" };
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, ToPages(documents), checkpoint).ConfigureAwait(false);

        verification.Breaks.Should().ContainSingle().Subject.Kind.Should().Be(AuditChainBreakKind.CheckpointHashMismatch);
        verification.MatchesLatestCheckpoint.Should().BeFalse();
    }

    [Fact]
    public async Task A_checkpoint_count_above_the_segment_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with { Count = 5 };
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointCountMismatch);
        verification.MatchesLatestCheckpoint.Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_covered_document_breaks_the_checkpoint_count()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        documents.RemoveAt(1);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (2L, AuditChainBreakKind.Gap),
                (3L, AuditChainBreakKind.PreviousHashMismatch),
                (3L, AuditChainBreakKind.CheckpointCountMismatch),
            ]);
    }

    [Fact]
    public async Task A_checkpoint_for_a_missing_sequence_breaks_the_checkpoint_hash()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with { Sequence = 5 };
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Should().Contain(breakItem =>
            breakItem.Sequence == 5 && breakItem.Kind == AuditChainBreakKind.CheckpointHashMismatch);
    }

    [Fact]
    public async Task A_signed_checkpoint_verifies()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var signer = new VerificationTestSigner([7, 8, 9, 10]);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme], signer);

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
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var signer = new VerificationTestSigner([7, 8, 9, 10]);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var tampered = checkpoint with { Hash = "bogus", Signature = signature };
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme], signer);

        var verification = await verifier.VerifyAsync(Segment, documents, tampered).ConfigureAwait(false);

        verification.CheckpointSignatureValid.Should().BeFalse();
        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Select(breakItem => breakItem.Kind)
            .Should().BeEquivalentTo(
            [
                AuditChainBreakKind.CheckpointHashMismatch,
                AuditChainBreakKind.CheckpointSignatureInvalid,
            ]);
    }

    [Fact]
    public async Task An_unsigned_checkpoint_with_a_signer_is_reported_as_an_invalid_signature()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<VerificationTestDocument>(
            [scheme], new VerificationTestSigner([7, 8, 9, 10]));

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
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeNull();
    }

    [Fact]
    public async Task Every_break_is_reported_not_only_the_first()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        documents.RemoveAt(1);
        documents[2].Action = "orders.refund-order";
        documents[3].HashScheme = "retired-v0";
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with { Count = 9 };
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, documents, checkpoint).ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Select(breakItem => breakItem.Kind).Should().Contain(
        [
            AuditChainBreakKind.Gap,
            AuditChainBreakKind.HashMismatch,
            AuditChainBreakKind.UnknownHashScheme,
            AuditChainBreakKind.CheckpointCountMismatch,
        ]);
        verification.Breaks.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public async Task An_empty_segment_misses_its_checkpoint()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, full);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyAsync(Segment, [], checkpoint).ConfigureAwait(false);

        verification.MatchesLatestCheckpoint.Should().BeFalse();
        verification.Breaks.Select(breakItem => breakItem.Kind)
            .Should().BeEquivalentTo(
            [
                AuditChainBreakKind.CheckpointHashMismatch,
                AuditChainBreakKind.CheckpointCountMismatch,
            ]);
    }

    [Fact]
    public async Task A_trusted_suffix_verifies_with_no_breaks()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full.GetRange(0, 2));
        List<VerificationTestDocument> suffix = full.GetRange(2, 3);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, suffix, trusted).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(3);
        verification.HeadSequence.Should().Be(5);
        verification.HeadHash.Should().Be(full[4].Hash);
        verification.LatestCheckpoint.Should().Be(trusted);
        verification.MatchesLatestCheckpoint.Should().BeTrue();
    }

    [Fact]
    public async Task A_trusted_suffix_verifies_by_pages_with_no_breaks()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full.GetRange(0, 2));
        List<VerificationTestDocument> suffix = full.GetRange(2, 3);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, ToPages(suffix), trusted).ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(3);
        verification.HeadSequence.Should().Be(5);
    }

    [Fact]
    public async Task A_trusted_suffix_ignores_a_gap_in_the_trusted_prefix()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        full.RemoveAt(1);
        var trusted = new AuditCheckpoint(
            Guid.NewGuid(), Segment, 3, 2, full[1].Hash!, scheme.Name, AuditCheckpointKind.Regular, DateTimeOffset.UtcNow);
        List<VerificationTestDocument> suffix = full.GetRange(2, 2);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, suffix, trusted).ConfigureAwait(false);

        // The gap at sequence two sits inside the trusted prefix, so only the suffix links are checked.
        verification.Breaks.Should().NotContain(breakItem => breakItem.Kind == AuditChainBreakKind.Gap);
    }

    [Fact]
    public async Task A_gap_in_the_trusted_suffix_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full.GetRange(0, 2));
        List<VerificationTestDocument> suffix = full.GetRange(2, 3);
        suffix.RemoveAt(1);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, suffix, trusted).ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Select(breakItem => (breakItem.Sequence, breakItem.Kind))
            .Should().BeEquivalentTo(
            [
                (4L, AuditChainBreakKind.Gap),
                (5L, AuditChainBreakKind.PreviousHashMismatch),
            ]);
    }

    [Fact]
    public async Task A_tampered_suffix_document_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 4).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full.GetRange(0, 2));
        List<VerificationTestDocument> suffix = full.GetRange(2, 2);
        suffix[1].Action = "orders.refund-order";
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, suffix, trusted).ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(4);
        single.Kind.Should().Be(AuditChainBreakKind.HashMismatch);
    }

    [Fact]
    public async Task A_relinked_suffix_head_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 4).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full.GetRange(0, 2));
        List<VerificationTestDocument> suffix = full.GetRange(2, 2);
        suffix[0].PreviousHash = "bogus";
        await VerificationTestData.RehashAsync(suffix[0], scheme).ConfigureAwait(false);
        suffix[1].PreviousHash = suffix[0].Hash;
        await VerificationTestData.RehashAsync(suffix[1], scheme).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, suffix, trusted).ConfigureAwait(false);

        // The suffix links internally, so only the link against the trusted hash breaks.
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.PreviousHashMismatch);
    }

    [Fact]
    public async Task An_empty_suffix_trusts_the_checkpoint()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier
            .VerifyFromAsync(Segment, [], trusted)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(0);
        verification.HeadSequence.Should().Be(trusted.Sequence);
        verification.HeadHash.Should().Be(trusted.Hash);
        verification.LatestCheckpoint.Should().Be(trusted);
    }

    [Fact]
    public async Task A_suffix_record_at_the_trusted_sequence_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var full = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor(Segment, full.GetRange(0, 2));
        List<VerificationTestDocument> suffix = full.GetRange(1, 2);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var verification = await verifier.VerifyFromAsync(Segment, suffix, trusted).ConfigureAwait(false);

        verification.Breaks.Should().Contain(breakItem =>
            breakItem.Sequence == 2 && breakItem.Kind == AuditChainBreakKind.Duplicate);
    }

    [Fact]
    public async Task A_trusted_checkpoint_for_another_segment_is_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        var trusted = VerificationTestData.CheckpointFor("other/2026", documents);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var act = () => verifier.VerifyFromAsync(Segment, documents, trusted);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public void A_lookup_with_a_null_scheme_is_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var lookup = new Dictionary<string, AuditHashScheme<VerificationTestDocument>>
        {
            [scheme.Name] = null!,
        };

        var act = () => new AuditChainVerifier<VerificationTestDocument>(lookup);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Schemes_with_a_repeated_name_are_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");

        var act = () => new AuditChainVerifier<VerificationTestDocument>([scheme, scheme]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task A_blank_segment_is_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 1).ConfigureAwait(false);
        var verifier = new AuditChainVerifier<VerificationTestDocument>([scheme]);

        var act = () => verifier.VerifyAsync("  ", documents);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads list documents by pages, standing in for the store page reads the streaming pass consumes.
    /// </summary>
    /// <param name="documents">The documents to read.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <returns>The documents in the order the list holds them.</returns>
    private static async IAsyncEnumerable<TDocument> ToPages<TDocument>(
        IReadOnlyList<TDocument> documents,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return document;
        }
    }
}
