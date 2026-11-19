using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using Microsoft.Extensions.DependencyInjection;
using MessagingPreviousHashLinkRule = LiteBus.Auditing.Hashing.PreviousHashLinkRule;
using MessagingSha256AuditHasher = LiteBus.Auditing.Hashing.Sha256AuditHasher;

namespace LiteBus.Auditing.UnitTests.Hashing;

/// <summary>
///     Verifies that v1 records written before framing verify unchanged, and that v2 records bind the position.
/// </summary>
public sealed class HashSchemeCompatibilityTests
{
    [Fact]
    public async Task The_v1_scheme_recomputes_the_hash_written_through_the_previous_release_path()
    {
        var document = new HashTestDocument
        {
            Segment = "orders/2026",
            Sequence = 2,
            PreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            HashScheme = AuditHashSchemeNames.Sha256V1,
            Action = "orders.step-2"
        };

        // The previous release path: the position-binding canonical form passed through unchanged.
        byte[] canonical = new PositionBindingCanonicalizer().Canonicalize(document);
        byte[] linked = new MessagingPreviousHashLinkRule().Link(
            new AuditLinkInput(document.Segment, document.Sequence, document.PreviousHash, canonical));
        document.Hash = await new MessagingSha256AuditHasher()
            .HashAsync(linked, CancellationToken.None)
            .ConfigureAwait(false);

        using ServiceProvider provider = HashingTestServices.ProviderWith<PositionBindingCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V1);

        string recomputed = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        recomputed.Should().Be(document.Hash);
    }

    [Fact]
    public async Task The_v2_scheme_verifies_a_record_written_with_business_bytes_only()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        var document = new HashTestDocument
        {
            Segment = "orders/2026",
            Sequence = 2,
            PreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            HashScheme = AuditHashSchemeNames.Sha256V2,
            Action = "orders.step-2"
        };
        document.Hash = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        string recomputed = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        recomputed.Should().Be(document.Hash);
    }

    [Fact]
    public async Task Replacing_the_previous_hash_breaks_the_v2_hash()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        var document = new HashTestDocument
        {
            Segment = "orders/2026",
            Sequence = 2,
            PreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            HashScheme = AuditHashSchemeNames.Sha256V2,
            Action = "orders.step-2"
        };
        document.Hash = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        document.PreviousHash = "d4735e3a265e16eee03f59718b9b5d03019c07d8b6c51f90da3a666eec13ab35";
        string recomputed = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        recomputed.Should().NotBe(document.Hash);
    }

    [Fact]
    public async Task Reordering_breaks_the_v2_hash()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        var document = new HashTestDocument
        {
            Segment = "orders/2026",
            Sequence = 2,
            PreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            HashScheme = AuditHashSchemeNames.Sha256V2,
            Action = "orders.step-2"
        };
        document.Hash = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        document.Sequence = 3;
        string recomputed = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        recomputed.Should().NotBe(document.Hash);
    }

    [Fact]
    public async Task Altering_the_business_field_breaks_the_v2_hash()
    {
        using ServiceProvider provider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var registry = new AuditHashSchemeRegistry(provider);
        var scheme = registry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2);

        var document = new HashTestDocument
        {
            Segment = "orders/2026",
            Sequence = 2,
            PreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            HashScheme = AuditHashSchemeNames.Sha256V2,
            Action = "orders.step-2"
        };
        document.Hash = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        document.Action = "orders.step-2-altered";
        string recomputed = await HashWithAsync(scheme, document, CancellationToken.None).ConfigureAwait(false);

        recomputed.Should().NotBe(document.Hash);
    }

    [Fact]
    public async Task The_two_shipped_schemes_hash_the_same_document_differently()
    {
        using ServiceProvider positionProvider = HashingTestServices.ProviderWith<PositionBindingCanonicalizer>();
        using ServiceProvider businessProvider = HashingTestServices.ProviderWith<BusinessOnlyCanonicalizer>();
        var positionRegistry = new AuditHashSchemeRegistry(positionProvider);
        var businessRegistry = new AuditHashSchemeRegistry(businessProvider);

        var document = new HashTestDocument
        {
            Segment = "orders/2026",
            Sequence = 2,
            PreviousHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            Action = "orders.step-2"
        };

        string v1 = await HashWithAsync(
                positionRegistry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V1),
                document,
                CancellationToken.None)
            .ConfigureAwait(false);
        string v2 = await HashWithAsync(
                businessRegistry.Resolve<HashTestDocument>(AuditHashSchemeNames.Sha256V2),
                document,
                CancellationToken.None)
            .ConfigureAwait(false);

        v2.Should().NotBe(v1);
    }

    /// <summary>
    ///     Hashes one document exactly as a chain writer would: canonicalize, link, then hash.
    /// </summary>
    /// <param name="scheme">The scheme the document names.</param>
    /// <param name="document">The document whose chain fields, other than the hash, are already set.</param>
    /// <param name="cancellationToken">The token used to cancel the hash.</param>
    /// <returns>The hash.</returns>
    private static async Task<string> HashWithAsync(
        AuditHashScheme<HashTestDocument> scheme,
        HashTestDocument document,
        CancellationToken cancellationToken)
    {
        byte[] canonical = scheme.Canonicalizer.Canonicalize(document);
        byte[] linked = scheme.LinkRule.Link(
            new AuditLinkInput(document.Segment!, document.Sequence, document.PreviousHash, canonical));

        return await scheme.Hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);
    }
}
