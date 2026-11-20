using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Verification;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.UnitTests.Verification;

/// <summary>
///     Verifies that the audit engine verifier streams segments and compares every checkpoint source.
/// </summary>
public sealed class AuditVerifierTests
{
    /// <summary>
    ///     The segment every chain in these tests is appended to.
    /// </summary>
    private const string Segment = "orders/2026";

    [Fact]
    public async Task An_intact_chain_with_a_stored_checkpoint_verifies()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(3);
        verification.HeadSequence.Should().Be(3);
        verification.HeadHash.Should().Be(documents[2].Hash);
        verification.LatestCheckpoint.Should().Be(checkpoint);
        verification.MatchesLatestCheckpoint.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeNull();
        var comparison = verification.Comparisons.Should().ContainSingle().Subject;
        comparison.Checkpoint.Should().Be(checkpoint);
        comparison.HashMatches.Should().BeTrue();
        comparison.CountMatches.Should().BeTrue();
        comparison.SourceUnreachable.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_segment_with_no_checkpoint_verifies_intact()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var storedSource = new VerificationTestSource();
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(0);
        verification.HeadSequence.Should().BeNull();
        verification.LatestCheckpoint.Should().BeNull();
        verification.MatchesLatestCheckpoint.Should().BeNull();
    }

    [Fact]
    public async Task Records_with_no_checkpoint_report_checkpoint_missing_once_the_policy_is_due()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var storedSource = new VerificationTestSource();
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            checkpointPolicy: CheckpointPolicies.EveryRecords(3));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointMissing);
    }

    [Fact]
    public async Task A_segment_younger_than_its_first_checkpoint_verifies_intact()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var storedSource = new VerificationTestSource();
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue("the default policy cuts the first checkpoint at 100 records");
        verification.RecordCount.Should().Be(3);
    }

    [Fact]
    public async Task Two_sources_in_agreement_verify_with_two_comparisons()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var fileSource = new FileVerificationTestSource();
        fileSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<FileVerificationTestSource>(fileSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource), typeof(FileVerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.Comparisons.Should().HaveCount(2);
        verification.Comparisons[0].HashMatches.Should().BeTrue();
        verification.Comparisons[0].CountMatches.Should().BeTrue();
        verification.Comparisons[1].HashMatches.Should().BeTrue();
        verification.Comparisons[1].CountMatches.Should().BeTrue();
        verification.Comparisons.Should().OnlyContain(comparison => !comparison.SourceUnreachable);
    }

    [Fact]
    public async Task A_disagreeing_source_reports_a_hash_mismatch_beside_its_comparison()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var storedCheckpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var fileCheckpoint = storedCheckpoint with { Hash = "bogus" };
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(storedCheckpoint);
        var fileSource = new FileVerificationTestSource();
        fileSource.SetCheckpoint(fileCheckpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<FileVerificationTestSource>(fileSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource), typeof(FileVerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointHashMismatch);
        single.Detail.Should().Contain(nameof(FileVerificationTestSource));
        verification.Comparisons.Should().HaveCount(2);
        verification.Comparisons[0].HashMatches.Should().BeTrue();
        verification.Comparisons[1].HashMatches.Should().BeFalse();
        verification.MatchesLatestCheckpoint.Should().BeTrue();
    }

    [Fact]
    public async Task A_source_counting_more_than_the_segment_reports_a_count_mismatch()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var storedCheckpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var fileCheckpoint = storedCheckpoint with { Count = 9 };
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(storedCheckpoint);
        var fileSource = new FileVerificationTestSource();
        fileSource.SetCheckpoint(fileCheckpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<FileVerificationTestSource>(fileSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource), typeof(FileVerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.Breaks.Should().ContainSingle().Subject.Kind
            .Should().Be(AuditChainBreakKind.CheckpointCountMismatch);
        verification.Comparisons[0].CountMatches.Should().BeTrue();
        verification.Comparisons[1].CountMatches.Should().BeFalse();
    }

    [Fact]
    public async Task An_unreachable_source_is_a_warning_not_a_break()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<VerificationUnreachableSource>(new VerificationUnreachableSource());
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource), typeof(VerificationUnreachableSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.Comparisons.Should().HaveCount(2);
        verification.Comparisons[0].SourceUnreachable.Should().BeFalse();
        verification.Comparisons[0].HashMatches.Should().BeTrue();
        var unreachable = verification.Comparisons[1];
        unreachable.SourceUnreachable.Should().BeTrue();
        unreachable.Checkpoint.Should().BeNull();
        unreachable.HashMatches.Should().BeNull();
        unreachable.CountMatches.Should().BeNull();
    }

    [Fact]
    public async Task An_unreachable_source_stays_apart_from_a_disagreeing_source()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var storedCheckpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var fileCheckpoint = storedCheckpoint with { Hash = "bogus" };
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(storedCheckpoint);
        var fileSource = new FileVerificationTestSource();
        fileSource.SetCheckpoint(fileCheckpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<FileVerificationTestSource>(fileSource);
        serviceProvider.Add<VerificationUnreachableSource>(new VerificationUnreachableSource());
        var options = VerificationTestData.ChainOptions(
            sourceTypes:
            [
                typeof(VerificationTestSource),
                typeof(FileVerificationTestSource),
                typeof(VerificationUnreachableSource),
            ]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Should().ContainSingle().Subject.Kind
            .Should().Be(AuditChainBreakKind.CheckpointHashMismatch);
        verification.Comparisons.Should().HaveCount(3);
        verification.Comparisons[1].SourceUnreachable.Should().BeFalse();
        verification.Comparisons[1].HashMatches.Should().BeFalse();
        verification.Comparisons[2].SourceUnreachable.Should().BeTrue();
    }

    [Fact]
    public async Task A_signed_checkpoint_verifies()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var signer = new VerificationTestSigner([7, 8, 9, 10]);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var signed = checkpoint with { Signature = signature };
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(signed);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<VerificationTestSigner>(signer);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            signerTypes: [typeof(VerificationTestSigner)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.CheckpointSignatureValid.Should().BeTrue();
        verification.Comparisons.Should().ContainSingle().Subject.SignatureValid.Should().BeTrue();
    }

    [Fact]
    public async Task An_unsigned_checkpoint_with_a_signer_reports_an_invalid_signature()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<VerificationTestSigner>(new VerificationTestSigner([7, 8, 9, 10]));
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            signerTypes: [typeof(VerificationTestSigner)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointSignatureInvalid);
        verification.CheckpointSignatureValid.Should().BeFalse();
        verification.MatchesLatestCheckpoint.Should().BeTrue();
    }

    [Fact]
    public async Task Every_signer_must_verify_the_checkpoint()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var first = new VerificationTestSigner([7, 8, 9, 10]);
        var second = new SecondVerificationTestSigner([1, 2, 3, 4]);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var signature = await first.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint with { Signature = signature });
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        serviceProvider.Add<VerificationTestSigner>(first);
        serviceProvider.Add<SecondVerificationTestSigner>(second);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            signerTypes: [typeof(VerificationTestSigner), typeof(SecondVerificationTestSigner)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        // The checkpoint carries the first signer's signature only, so the second signer rejects it.
        verification.Breaks.Should().ContainSingle().Subject.Kind
            .Should().Be(AuditChainBreakKind.CheckpointSignatureInvalid);
        verification.CheckpointSignatureValid.Should().BeFalse();
    }

    [Fact]
    public async Task A_stale_checkpoint_with_records_after_it_is_reported()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(
            Segment, documents.GetRange(0, 2), now.AddDays(-10));
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            checkpointStaleAfter: TimeSpan.FromDays(1));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        var stale = verification.Breaks.Should().ContainSingle().Subject;
        stale.Sequence.Should().Be(2);
        stale.Kind.Should().Be(AuditChainBreakKind.CheckpointStale);
    }

    [Fact]
    public async Task A_stale_checkpoint_with_no_records_after_it_is_not_reported()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents, now.AddDays(-10));
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            checkpointStaleAfter: TimeSpan.FromDays(1));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task A_fresh_checkpoint_with_records_after_it_is_not_stale()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(
            Segment, documents.GetRange(0, 2), now.AddHours(-1));
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            checkpointStaleAfter: TimeSpan.FromDays(1));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.Breaks.Should().NotContain(breakItem => breakItem.Kind == AuditChainBreakKind.CheckpointStale);
    }

    [Fact]
    public async Task The_request_stale_threshold_overrides_the_configured_one()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(
            Segment, documents.GetRange(0, 2), now.AddDays(-10));
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(
                new AuditChainVerificationRequest(Segment, CheckpointStaleAfter: TimeSpan.FromDays(1)),
                CancellationToken.None)
            .ConfigureAwait(false);

        verification.Breaks.Should().ContainSingle().Subject.Kind
            .Should().Be(AuditChainBreakKind.CheckpointStale);
    }

    [Fact]
    public async Task A_pending_checkpoint_is_reported_as_unpublished()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with
        {
            PublishState = CheckpointPublishState.Pending,
            PublishAttempts = 2,
            LastPublishError = "The anchor refused the write.",
        };
        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(3);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointUnpublished);
        single.Detail.Should().Contain("The anchor refused the write.");
    }

    [Fact]
    public async Task A_published_checkpoint_is_not_reported_as_unpublished()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents) with
        {
            PublishState = CheckpointPublishState.Published,
            PublishAttempts = 1,
        };
        await store.StoreCheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task A_record_unlinked_past_the_threshold_is_reported()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        await store
            .StageAsync(Segment, new VerificationTestDocument { Action = "orders.staged" }, CancellationToken.None)
            .ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            unlinkedAfter: TimeSpan.FromMinutes(5));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(0);
        single.Kind.Should().Be(AuditChainBreakKind.Unlinked);
    }

    [Fact]
    public async Task A_freshly_staged_record_is_not_reported_as_unlinked()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        await store
            .StageAsync(Segment, new VerificationTestDocument { Action = "orders.staged" }, CancellationToken.None)
            .ConfigureAwait(false);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            unlinkedAfter: TimeSpan.FromMinutes(5));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.Breaks.Should().NotContain(breakItem => breakItem.Kind == AuditChainBreakKind.Unlinked);
    }

    [Fact]
    public async Task The_request_unlinked_threshold_overrides_the_configured_one()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(now);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 2).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        await store
            .StageAsync(Segment, new VerificationTestDocument { Action = "orders.staged" }, CancellationToken.None)
            .ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(
            sourceTypes: [typeof(VerificationTestSource)],
            unlinkedAfter: TimeSpan.FromMinutes(5));
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(
                new AuditChainVerificationRequest(Segment, UnlinkedOlderThan: TimeSpan.FromHours(1)),
                CancellationToken.None)
            .ConfigureAwait(false);

        verification.Breaks.Should().NotContain(breakItem => breakItem.Kind == AuditChainBreakKind.Unlinked);
    }

    [Fact]
    public async Task Verify_from_a_trusted_sequence_checks_only_the_suffix()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 5).ConfigureAwait(false);
        documents.RemoveAt(1);
        store.SeedLinked(Segment, documents);
        var headCheckpoint = new AuditCheckpoint(
            Guid.NewGuid(), Segment, 5, 4, documents[3].Hash!, scheme.Name, AuditCheckpointKind.Regular, DateTimeOffset.UtcNow);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(headCheckpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment, FromSequence: 3), CancellationToken.None)
            .ConfigureAwait(false);

        // The gap at sequence two sits inside the trusted prefix, so the suffix verifies intact.
        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(2);
        verification.HeadSequence.Should().Be(5);
    }

    [Fact]
    public async Task Verify_from_a_trusted_sequence_reports_a_suffix_tamper()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 4).ConfigureAwait(false);
        documents[3].Action = "orders.refund-order";
        store.SeedLinked(Segment, documents);
        var headCheckpoint = new AuditCheckpoint(
            Guid.NewGuid(), Segment, 4, 4, documents[3].Hash!, scheme.Name, AuditCheckpointKind.Regular, DateTimeOffset.UtcNow);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(headCheckpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment, FromSequence: 2), CancellationToken.None)
            .ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(4);
        single.Kind.Should().Be(AuditChainBreakKind.HashMismatch);
    }

    [Fact]
    public async Task Verify_from_a_missing_trusted_sequence_reports_checkpoint_missing()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment, FromSequence: 10), CancellationToken.None)
            .ConfigureAwait(false);

        var single = verification.Breaks.Should().ContainSingle().Subject;
        single.Sequence.Should().Be(10);
        single.Kind.Should().Be(AuditChainBreakKind.CheckpointMissing);
        verification.RecordCount.Should().Be(0);
    }

    [Fact]
    public async Task Verify_from_the_head_trusts_the_whole_segment()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment, FromSequence: 3), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(0);
        verification.HeadSequence.Should().Be(3);
        verification.HeadHash.Should().Be(documents[2].Hash);
    }

    [Fact]
    public async Task An_unknown_scheme_from_the_registry_is_reported()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        documents[2].HashScheme = "retired-v0";
        store.SeedLinked(Segment, documents);
        var storedSource = new VerificationTestSource();
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.Breaks.Should().Contain(breakItem =>
            breakItem.Sequence == 3 && breakItem.Kind == AuditChainBreakKind.UnknownHashScheme);
    }

    [Fact]
    public async Task A_large_segment_verifies_by_pages()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 300).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeTrue();
        verification.RecordCount.Should().Be(300);
        verification.HeadSequence.Should().Be(300);
    }

    [Fact]
    public async Task A_tampered_chain_breaks_its_stored_comparison()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var timeProvider = new VerificationTestTimeProvider(DateTimeOffset.UtcNow);
        var store = new VerificationTestStore(timeProvider);
        var documents = await VerificationTestData.BuildChainAsync(Segment, scheme, 3).ConfigureAwait(false);
        store.SeedLinked(Segment, documents);
        var checkpoint = VerificationTestData.CheckpointFor(Segment, documents);
        documents[2].Action = "orders.refund-order";
        var storedSource = new VerificationTestSource();
        storedSource.SetCheckpoint(checkpoint);
        var serviceProvider = new VerificationTestServiceProvider();
        serviceProvider.Add<VerificationTestSource>(storedSource);
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options, serviceProvider, timeProvider);

        var verification = await verifier
            .VerifyAsync(new AuditChainVerificationRequest(Segment), CancellationToken.None)
            .ConfigureAwait(false);

        verification.IsIntact.Should().BeFalse();
        verification.Breaks.Should().Contain(breakItem =>
            breakItem.Kind == AuditChainBreakKind.HashMismatch);
        verification.MatchesLatestCheckpoint.Should().BeTrue();
    }

    [Fact]
    public async Task A_blank_segment_is_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var store = new VerificationTestStore();
        var options = VerificationTestData.ChainOptions();
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options);

        var act = () => verifier.VerifyAsync(
            new AuditChainVerificationRequest("  "), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_non_positive_from_sequence_is_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var store = new VerificationTestStore();
        var options = VerificationTestData.ChainOptions();
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options);

        var act = () => verifier.VerifyAsync(
            new AuditChainVerificationRequest(Segment, FromSequence: 0), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task A_null_request_is_rejected()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var store = new VerificationTestStore();
        var options = VerificationTestData.ChainOptions();
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store, new VerificationTestSchemeRegistry([scheme]), options);

        var act = () => verifier.VerifyAsync(null!, CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task An_unregistered_source_type_fails_resolution()
    {
        var scheme = VerificationTestData.TestScheme("sha256-v1");
        var store = new VerificationTestStore();
        var options = VerificationTestData.ChainOptions(sourceTypes: [typeof(VerificationTestSource)]);
        var verifier = new AuditVerifier<VerificationTestDocument>(
            store,
            new VerificationTestSchemeRegistry([scheme]),
            options,
            new VerificationTestServiceProvider());

        var act = () => verifier.VerifyAsync(
            new AuditChainVerificationRequest(Segment), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<LiteBusDependencyResolutionException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     A second checkpoint source type holding its own fixed checkpoint, so multi-source tests register two sources.
    /// </summary>
    private sealed class FileVerificationTestSource : VerificationTestSource
    {
    }

    /// <summary>
    ///     A second checkpoint signer type with its own key, so multi-signer tests register two signers.
    /// </summary>
    private sealed class SecondVerificationTestSigner : VerificationTestSigner
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SecondVerificationTestSigner" /> class.
        /// </summary>
        /// <param name="key">The key the test signatures are computed with.</param>
        public SecondVerificationTestSigner(byte[] key)
            : base(key)
        {
        }
    }
}
