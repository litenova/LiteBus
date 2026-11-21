using System;
using LiteBus.Auditing.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Proves at composition that an audit hash scheme binds the chain position into every hash.
/// </summary>
/// <remarks>
///     <para>
///         A link rule that leaves position binding to the canonicalizer verifies without being linked when the
///         canonical form covers business fields only: records can be reordered or replaced without a break, and
///         nothing reports it. The self-test changes the segment, the sequence, and the previous hash of a fixed
///         sample and fails composition when any of them leaves the output unchanged, so a misbound scheme never
///         reaches runtime.
///     </para>
///     <para>
///         The auditing module runs the synchronous check for the write scheme while it builds. The asynchronous check
///         proves the same binding end to end through the hasher, and the unit tests run it for every shipped scheme.
///     </para>
/// </remarks>
public static class AuditHashSchemeSelfTest
{
    /// <summary>
    ///     The segment of the fixed sample the checks mutate.
    /// </summary>
    private const string SampleSegment = "audit/self-test";

    /// <summary>
    ///     The sequence of the fixed sample the checks mutate.
    /// </summary>
    private const long SampleSequence = 7;

    /// <summary>
    ///     The previous hash of the fixed sample the checks mutate.
    /// </summary>
    private const string SamplePreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    /// <summary>
    ///     The segment the sample moves to. Fixed so the check is deterministic.
    /// </summary>
    private const string MutatedSegment = "audit/self-test-moved";

    /// <summary>
    ///     The sequence the sample moves to. Fixed so the check is deterministic.
    /// </summary>
    private const long MutatedSequence = 8;

    /// <summary>
    ///     The previous hash the sample moves to. Fixed so the check is deterministic.
    /// </summary>
    private const string MutatedPreviousHash = "d4735e3a265e16eee03f59718b9b5d03019c07d8b6c51f90da3a666eec13ab35";

    /// <summary>
    ///     Proves that a link rule mixes the segment, sequence, previous hash, and canonical bytes into its output.
    /// </summary>
    /// <param name="schemeName">The scheme the rule belongs to, named when the check fails.</param>
    /// <param name="linkRule">The rule under check.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     Changing the segment, sequence, previous hash, or canonical bytes of the sample leaves the linked bytes
    ///     unchanged, or the rule returns no bytes.
    /// </exception>
    /// <remarks>
    ///     The check compares linked bytes rather than hashes: distinct bytes hash distinctly, so a rule that mixes
    ///     every field into its output binds every field into the hash. It runs synchronously so the auditing module
    ///     can call it while it builds.
    /// </remarks>
    public static void VerifyPositionBinding(string schemeName, IAuditLinkRule linkRule)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(linkRule);

        byte[] baseline = LinkOrThrow(schemeName, linkRule, DefaultSampleInput());

        byte[] movedSegment = LinkOrThrow(
            schemeName, linkRule, SampleInput(MutatedSegment, SampleSequence, SamplePreviousHash, SampleCanonicalBytes()));

        if (baseline.SequenceEqual(movedSegment))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the segment: changing the segment of a sample " +
                "left the linked bytes unchanged, so records could be moved between segments without breaking the " +
                "chain. Use a link rule that frames the chain position, such as FramedLinkRule.");
        }

        byte[] movedSequence = LinkOrThrow(
            schemeName, linkRule, SampleInput(SampleSegment, MutatedSequence, SamplePreviousHash, SampleCanonicalBytes()));

        if (baseline.SequenceEqual(movedSequence))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the sequence: changing the sequence of a sample " +
                "left the linked bytes unchanged, so records could be reordered without breaking the chain. Use a " +
                "link rule that frames the chain position, such as FramedLinkRule.");
        }

        byte[] movedPreviousHash = LinkOrThrow(
            schemeName, linkRule, SampleInput(SampleSegment, SampleSequence, MutatedPreviousHash, SampleCanonicalBytes()));

        if (baseline.SequenceEqual(movedPreviousHash))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the previous hash: changing the previous hash of a " +
                "sample left the linked bytes unchanged, so records could be replaced without breaking the chain. " +
                "Use a link rule that frames the chain position, such as FramedLinkRule.");
        }

        byte[] movedCanonicalBytes = LinkOrThrow(
            schemeName, linkRule, SampleInput(SampleSegment, SampleSequence, SamplePreviousHash, MutatedCanonicalBytes()));

        if (baseline.SequenceEqual(movedCanonicalBytes))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the canonical bytes: changing the canonical bytes " +
                "of a sample left the linked bytes unchanged, so records could be altered without breaking the " +
                "chain. Use a link rule that carries the canonical bytes, such as FramedLinkRule.");
        }
    }

    /// <summary>
    ///     Proves that a link rule factory binds the scheme name into its output.
    /// </summary>
    /// <param name="firstSchemeName">The first scheme to frame the sample with.</param>
    /// <param name="secondSchemeName">The second scheme to frame the sample with. Must differ from the first.</param>
    /// <param name="linkRuleFactory">The factory that builds the rule framing the scheme it is given.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     Framing the same sample under two scheme names yields the same linked bytes, or the factory returns no
    ///     rule.
    /// </exception>
    /// <remarks>
    ///     The scheme name versions the framing: without it, a record hashed under one scheme could verify under
    ///     another, and a scheme upgrade would silently recompute history instead of failing loudly.
    /// </remarks>
    public static void VerifySchemeBinding(
        string firstSchemeName,
        string secondSchemeName,
        Func<string, IAuditLinkRule> linkRuleFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSchemeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secondSchemeName);
        ArgumentNullException.ThrowIfNull(linkRuleFactory);

        if (string.Equals(firstSchemeName, secondSchemeName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The two scheme names must differ.", nameof(secondSchemeName));
        }

        var input = DefaultSampleInput();

        IAuditLinkRule firstRule = linkRuleFactory(firstSchemeName)
            ?? throw new LiteBusConfigurationException(
                $"The audit link rule factory returned no rule for scheme '{firstSchemeName}'.");
        IAuditLinkRule secondRule = linkRuleFactory(secondSchemeName)
            ?? throw new LiteBusConfigurationException(
                $"The audit link rule factory returned no rule for scheme '{secondSchemeName}'.");

        byte[] first = LinkOrThrow(firstSchemeName, firstRule, input);
        byte[] second = LinkOrThrow(secondSchemeName, secondRule, input);

        if (first.SequenceEqual(second))
        {
            throw new LiteBusConfigurationException(
                $"The audit link rule does not bind the scheme name: framing a sample as '{firstSchemeName}' and as " +
                $"'{secondSchemeName}' yielded the same linked bytes. Pass the scheme name to the rule, as " +
                "FramedLinkRule does.");
        }
    }

    /// <summary>
    ///     Proves end to end that changing the chain position of a sample changes its hash.
    /// </summary>
    /// <param name="schemeName">The scheme the rule and hasher belong to, named when the check fails.</param>
    /// <param name="linkRule">The rule under check.</param>
    /// <param name="hasher">The hasher that hashes the linked bytes.</param>
    /// <param name="cancellationToken">The token used to cancel the check.</param>
    /// <returns>A task representing the asynchronous check.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     Changing the segment, sequence, or previous hash of the sample leaves the hash unchanged.
    /// </exception>
    public static async ValueTask VerifyHashBindingAsync(
        string schemeName,
        IAuditLinkRule linkRule,
        IAuditHasher hasher,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);
        ArgumentNullException.ThrowIfNull(linkRule);
        ArgumentNullException.ThrowIfNull(hasher);
        cancellationToken.ThrowIfCancellationRequested();

        string baseline = await HashAsync(schemeName, linkRule, hasher, DefaultSampleInput(), cancellationToken)
            .ConfigureAwait(false);

        string movedSegment = await HashAsync(
                schemeName,
                linkRule,
                hasher,
                SampleInput(MutatedSegment, SampleSequence, SamplePreviousHash, SampleCanonicalBytes()),
                cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(baseline, movedSegment, StringComparison.Ordinal))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the segment: changing the segment of a sample " +
                "left the hash unchanged.");
        }

        string movedSequence = await HashAsync(
                schemeName,
                linkRule,
                hasher,
                SampleInput(SampleSegment, MutatedSequence, SamplePreviousHash, SampleCanonicalBytes()),
                cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(baseline, movedSequence, StringComparison.Ordinal))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the sequence: changing the sequence of a sample " +
                "left the hash unchanged.");
        }

        string movedPreviousHash = await HashAsync(
                schemeName,
                linkRule,
                hasher,
                SampleInput(SampleSegment, SampleSequence, MutatedPreviousHash, SampleCanonicalBytes()),
                cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(baseline, movedPreviousHash, StringComparison.Ordinal))
        {
            throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' does not bind the previous hash: changing the previous hash of " +
                "a sample left the hash unchanged.");
        }
    }

    /// <summary>
    ///     Builds the fixed sample the checks mutate.
    /// </summary>
    /// <returns>The link input every check starts from.</returns>
    private static AuditLinkInput DefaultSampleInput()
    {
        return SampleInput(SampleSegment, SampleSequence, SamplePreviousHash, SampleCanonicalBytes());
    }

    /// <summary>
    ///     Builds one link input from explicit fields.
    /// </summary>
    /// <param name="segment">The segment to frame.</param>
    /// <param name="sequence">The sequence to frame.</param>
    /// <param name="previousHash">The previous hash to frame.</param>
    /// <param name="canonicalBytes">The canonical bytes to frame.</param>
    /// <returns>The link input.</returns>
    private static AuditLinkInput SampleInput(string segment, long sequence, string? previousHash, byte[] canonicalBytes)
    {
        return new AuditLinkInput(segment, sequence, previousHash, canonicalBytes);
    }

    /// <summary>
    ///     Builds the canonical bytes of the fixed sample.
    /// </summary>
    /// <returns>Eight fixed bytes. Built per call because link inputs hold the array reference.</returns>
    private static byte[] SampleCanonicalBytes()
    {
        return [8, 7, 6, 5, 4, 3, 2, 1];
    }

    /// <summary>
    ///     Builds canonical bytes that differ from the fixed sample in every position.
    /// </summary>
    /// <returns>Eight fixed bytes that share no position with the sample bytes.</returns>
    private static byte[] MutatedCanonicalBytes()
    {
        return [1, 2, 3, 4, 5, 6, 7, 8];
    }

    /// <summary>
    ///     Links one sample, failing composition when the rule returns no bytes.
    /// </summary>
    /// <param name="schemeName">The scheme the rule belongs to, named when the rule misbehaves.</param>
    /// <param name="linkRule">The rule under check.</param>
    /// <param name="input">The sample to link.</param>
    /// <returns>The linked bytes.</returns>
    /// <exception cref="LiteBusConfigurationException">The rule returned no bytes.</exception>
    private static byte[] LinkOrThrow(string schemeName, IAuditLinkRule linkRule, AuditLinkInput input)
    {
        return linkRule.Link(input)
            ?? throw new LiteBusConfigurationException(
                $"Audit hash scheme '{schemeName}' returned no linked bytes for its sample. A link rule must " +
                "return the bytes the hasher receives.");
    }

    /// <summary>
    ///     Links and hashes one sample.
    /// </summary>
    /// <param name="schemeName">The scheme the rule and hasher belong to, named when the rule misbehaves.</param>
    /// <param name="linkRule">The rule under check.</param>
    /// <param name="hasher">The hasher that hashes the linked bytes.</param>
    /// <param name="input">The sample to hash.</param>
    /// <param name="cancellationToken">The token used to cancel the hash.</param>
    /// <returns>The hash of the sample.</returns>
    private static async ValueTask<string> HashAsync(
        string schemeName,
        IAuditLinkRule linkRule,
        IAuditHasher hasher,
        AuditLinkInput input,
        CancellationToken cancellationToken)
    {
        byte[] linked = LinkOrThrow(schemeName, linkRule, input);

        return await hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);
    }
}
