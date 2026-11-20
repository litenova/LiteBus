using System.Security.Cryptography;
using LiteBus.Auditing.Abstractions;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LiteBus.Auditing.Anchors.Timestamping;

namespace LiteBus.Auditing.UnitTests.Anchors;

/// <summary>
///     Verifies that the RFC 3161 checkpoint signer time-stamps checkpoint hashes and verifies their tokens.
/// </summary>
public sealed class Rfc3161CheckpointSignerTests
{
    /// <summary>
    ///     Builds the time-stamping authority URI tests post queries to.
    /// </summary>
    /// <returns>The test authority URI. Never reached over the network.</returns>
    private static Uri Authority() => new("https://tsa.example.test/timestamp");

    /// <summary>
    ///     Hashes test text the way a chain hashes linked bytes.
    /// </summary>
    /// <param name="text">The text to hash.</param>
    /// <returns>The lowercase hexadecimal SHA-256 digest.</returns>
    private static string HashOf(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    ///     Builds a checkpoint covering the given hash.
    /// </summary>
    /// <param name="hash">The checkpoint hash.</param>
    /// <returns>A checkpoint over the test segment at sequence seven.</returns>
    private static AuditCheckpoint Checkpoint(string hash)
    {
        return new AuditCheckpoint(
            Guid.NewGuid(),
            "orders",
            7,
            7,
            hash,
            "sha256-v2",
            AuditCheckpointKind.Regular,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Creates a self-signed certificate the local authority signs tokens with.
    /// </summary>
    /// <returns>A time-stamping certificate with a private key.</returns>
    private static X509Certificate2 CreateTimestampAuthorityCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            "CN=LiteBus Test Timestamp Authority",
            key,
            HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") },
            true));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));
    }

    /// <summary>
    ///     Builds signer options against the test authority.
    /// </summary>
    /// <param name="trustedSigner">The pinned authority certificate, or <see langword="null" /> for none.</param>
    /// <param name="timeout">The per-request timeout, or <see langword="null" /> for the default.</param>
    /// <returns>The signer options.</returns>
    private static Rfc3161TimestampOptions CreateOptions(
        X509Certificate2? trustedSigner = null,
        TimeSpan? timeout = null)
    {
        return new Rfc3161TimestampOptions(Authority())
        {
            Timeout = timeout ?? Rfc3161TimestampOptions.DefaultTimeout,
            TrustedSignerCertificates = trustedSigner is null ? [] : [trustedSigner],
        };
    }

    [Fact]
    public async Task Sign_posts_the_checkpoint_hash_and_returns_its_token()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7"));

        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        signature.Scheme.Should().Be(Rfc3161CheckpointSigner.SignatureScheme);
        signature.Value.Should().NotBeNullOrWhiteSpace();
        handler.RequestCount.Should().Be(1);
        handler.LastContentType.Should().Be("application/timestamp-query");
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.GetMessageHash().ToArray()
            .Should().BeEquivalentTo(Convert.FromHexString(checkpoint.Hash));

        var tokenBytes = Convert.FromBase64String(signature.Value);
        Rfc3161TimestampToken.TryDecode(tokenBytes, out var token, out _).Should().BeTrue();
        token.Should().NotBeNull();
        token!.TokenInfo.GetMessageHash().ToArray()
            .Should().BeEquivalentTo(Convert.FromHexString(checkpoint.Hash));
        token.TokenInfo.GetNonce().HasValue.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_accepts_a_signature_from_the_signatures_list()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7"));

        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var signed = checkpoint with { Signatures = [signature] };

        var valid = await signer.VerifyAsync(signed, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_accepts_a_signature_from_the_single_signature_slot()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7"));

        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var signed = checkpoint with { Signature = signature };

        var valid = await signer.VerifyAsync(signed, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_rejects_a_checkpoint_whose_hash_changed_after_signing()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7"));

        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var tampered = checkpoint with { Signatures = [signature], Hash = HashOf("orders-7-tampered") };

        var valid = await signer.VerifyAsync(tampered, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_returns_false_without_a_timestamp_signature()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);

        var unsigned = await signer
            .VerifyAsync(Checkpoint(HashOf("orders-7")), CancellationToken.None)
            .ConfigureAwait(false);
        var otherScheme = await signer
            .VerifyAsync(
                Checkpoint(HashOf("orders-7")) with
                {
                    Signatures = [new AuditCheckpointSignature("hmac-sha256", "00")],
                },
                CancellationToken.None)
            .ConfigureAwait(false);

        unsigned.Should().BeFalse();
        otherScheme.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_returns_false_when_the_signature_value_is_not_base64()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7")) with
        {
            Signatures = [new AuditCheckpointSignature(Rfc3161CheckpointSigner.SignatureScheme, "!!!")],
        };

        var valid = await signer.VerifyAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_returns_false_when_the_signature_value_is_not_a_token()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7")) with
        {
            Signatures =
            [
                new AuditCheckpointSignature(
                    Rfc3161CheckpointSigner.SignatureScheme,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))),
            ],
        };

        var valid = await signer.VerifyAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_accepts_a_token_from_a_pinned_authority()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(certificate), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7"));

        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var signed = checkpoint with { Signatures = [signature] };

        var valid = await signer.VerifyAsync(signed, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_rejects_a_token_from_an_authority_that_is_not_pinned()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var other = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(other), httpClient);
        var checkpoint = Checkpoint(HashOf("orders-7"));

        var signature = await signer.SignAsync(checkpoint, CancellationToken.None).ConfigureAwait(false);
        var signed = checkpoint with { Signatures = [signature] };

        var valid = await signer.VerifyAsync(signed, CancellationToken.None).ConfigureAwait(false);

        valid.Should().BeFalse();
    }

    [Fact]
    public async Task Sign_throws_when_the_authority_rejects_the_query()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(
            certificate,
            LocalTimestampAuthorityMode.FailureStatus);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);

        var act = () => signer.SignAsync(Checkpoint(HashOf("orders-7")), CancellationToken.None).AsTask();

        var failure = await act.Should().ThrowAsync<Rfc3161TimestampException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain("orders");
    }

    [Fact]
    public async Task Sign_throws_when_the_authority_cannot_be_reached()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(
            certificate,
            LocalTimestampAuthorityMode.Unreachable);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);

        var act = () => signer.SignAsync(Checkpoint(HashOf("orders-7")), CancellationToken.None).AsTask();

        var failure = await act.Should().ThrowAsync<Rfc3161TimestampException>().ConfigureAwait(false);
        failure.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Sign_throws_when_the_authority_answers_with_an_http_error()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(
            certificate,
            LocalTimestampAuthorityMode.HttpError);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);

        var act = () => signer.SignAsync(Checkpoint(HashOf("orders-7")), CancellationToken.None).AsTask();

        var failure = await act.Should().ThrowAsync<Rfc3161TimestampException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain("500");
    }

    [Fact]
    public async Task Sign_times_out_when_the_authority_never_answers()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(
            certificate,
            LocalTimestampAuthorityMode.NeverRespond);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(
            CreateOptions(timeout: TimeSpan.FromMilliseconds(50)),
            httpClient);

        var act = () => signer.SignAsync(Checkpoint(HashOf("orders-7")), CancellationToken.None).AsTask();

        var failure = await act.Should().ThrowAsync<Rfc3161TimestampException>().ConfigureAwait(false);
        failure.Which.Message.Should().Contain("did not answer");
    }

    [Fact]
    public async Task Sign_throws_when_the_checkpoint_hash_is_not_hexadecimal()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);

        var act = () => signer.SignAsync(Checkpoint("not-hexadecimal!!"), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<Rfc3161TimestampException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Sign_throws_when_the_checkpoint_carries_no_hash()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);

        var act = () => signer.SignAsync(Checkpoint(string.Empty), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<Rfc3161TimestampException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task Sign_honors_cancellation()
    {
        using var certificate = CreateTimestampAuthorityCertificate();
        using var handler = new LocalTimestampAuthorityHandler(certificate);
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var signer = new Rfc3161CheckpointSigner(CreateOptions(), httpClient);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);

        var act = () => signer.SignAsync(Checkpoint(HashOf("orders-7")), cancelled.Token).AsTask();

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
    }

    [Fact]
    public void Null_options_are_rejected()
    {
        var act = () => new Rfc3161CheckpointSigner(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Relative_authority_is_rejected()
    {
        var act = () => new Rfc3161TimestampOptions(new Uri("/timestamp", UriKind.Relative));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Non_http_authority_is_rejected()
    {
        var act = () => new Rfc3161TimestampOptions(new Uri("ftp://tsa.example.test/"));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Non_positive_timeout_is_rejected()
    {
        var act = () => new Rfc3161CheckpointSigner(
            new Rfc3161TimestampOptions(Authority()) { Timeout = TimeSpan.Zero });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Unknown_hash_algorithm_is_rejected()
    {
        var act = () => new Rfc3161CheckpointSigner(
            new Rfc3161TimestampOptions(Authority())
            {
                HashAlgorithm = new HashAlgorithmName("not-a-hash-algorithm"),
            });

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Null_trusted_certificate_is_rejected()
    {
        var act = () => new Rfc3161CheckpointSigner(
            new Rfc3161TimestampOptions(Authority()) { TrustedSignerCertificates = [null!] });

        act.Should().Throw<ArgumentException>();
    }
}
