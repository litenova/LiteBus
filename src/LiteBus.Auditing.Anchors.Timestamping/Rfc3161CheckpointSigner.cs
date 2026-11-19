using System;
using LiteBus.Auditing.Abstractions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing.Anchors.Timestamping;

/// <summary>
///     Signs audit checkpoints with time-stamp tokens from an RFC 3161 time-stamping authority.
/// </summary>
/// <remarks>
///     <para>
///         Signing posts an <see cref="Rfc3161TimestampRequest" /> for the checkpoint hash to the configured
///         authority and returns the issued token as an <see cref="AuditCheckpointSignature" /> under
///         <see cref="SignatureScheme" />, so the token sits beside the log signature and witness cosignatures
///         on the checkpoint. The request carries a random nonce, and the response is combined with the request
///         that produced it, so a replayed token for another checkpoint is rejected.
///     </para>
///     <para>
///         The signature value is the Base64 encoding of the DER-encoded time-stamp token. Verification decodes
///         it, checks that the token covers the checkpoint hash, and, when
///         <see cref="Rfc3161TimestampOptions.TrustedSignerCertificates" /> pins authority certificates, checks
///         that the token signer is one of them. Verification makes no network call.
///     </para>
///     <para>
///         The signer is thread-safe: the options are immutable once constructed, and the HTTP client is shared
///         across calls. An application that needs proxying, authentication headers, or handler lifetime tuning
///         passes its own <see cref="HttpClient" />; otherwise the signer keeps one client for its own lifetime.
///     </para>
/// </remarks>
public sealed class Rfc3161CheckpointSigner : ICheckpointSigner
{
    /// <summary>
    ///     The scheme an <see cref="AuditCheckpointSignature" /> carries when its value is an RFC 3161 time-stamp
    ///     token. Stored on checkpoints, so it is part of the persisted contract.
    /// </summary>
    public const string SignatureScheme = "rfc3161";

    /// <summary>
    ///     The media type of an RFC 3161 timestamp query posted to the authority.
    /// </summary>
    private const string TimestampQueryMediaType = "application/timestamp-query";

    /// <summary>
    ///     How many random bytes the request nonce holds.
    /// </summary>
    private const int NonceSizeInBytes = 8;

    /// <summary>
    ///     The authority and token settings. Held for the life of the signer.
    /// </summary>
    private readonly Rfc3161TimestampOptions _options;

    /// <summary>
    ///     The client timestamp queries are posted with. Held for the life of the signer.
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    ///     The pinned authority certificates, copied at construction. Empty when any valid signer is accepted.
    /// </summary>
    private readonly X509Certificate2[] _trustedSignerCertificates;

    /// <summary>
    ///     The pinned certificates as token verification candidates, or <see langword="null" /> when nothing is
    ///     pinned. Held for the life of the signer.
    /// </summary>
    private readonly X509Certificate2Collection? _extraCandidates;

    /// <summary>
    ///     The logger timestamp events are written to, or <see langword="null" /> for none. Held for the life of
    ///     the signer.
    /// </summary>
    private readonly ILogger<Rfc3161CheckpointSigner>? _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Rfc3161CheckpointSigner" /> class.
    /// </summary>
    /// <param name="options">The time-stamping authority and token settings.</param>
    /// <param name="httpClient">
    ///     The client timestamp queries are posted with, or <see langword="null" /> for a client the signer keeps
    ///     for its own lifetime.
    /// </param>
    /// <param name="logger">The logger timestamp events are written to, or <see langword="null" /> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The configured timeout is zero or negative.</exception>
    /// <exception cref="ArgumentException">
    ///     The trusted signer certificates contain a <see langword="null" /> entry, or the hash algorithm is not a
    ///     known hash algorithm.
    /// </exception>
    public Rfc3161CheckpointSigner(
        Rfc3161TimestampOptions options,
        HttpClient? httpClient = null,
        ILogger<Rfc3161CheckpointSigner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Timeout, TimeSpan.Zero);

        foreach (var pinned in options.TrustedSignerCertificates)
        {
            if (pinned is null)
            {
                throw new ArgumentException(
                    "The trusted signer certificates must not contain a null entry.",
                    nameof(options));
            }
        }

        try
        {
            _ = Rfc3161TimestampRequest.CreateFromHash(
                new byte[32],
                options.HashAlgorithm,
                null,
                null,
                options.RequestSignerCertificates,
                null);
        }
        catch (CryptographicException exception)
        {
            throw new ArgumentException(
                $"The timestamp options name the hash algorithm '{options.HashAlgorithm.Name}', which is not a known hash algorithm.",
                nameof(options),
                exception);
        }

        _options = options;
        _httpClient = httpClient ?? new HttpClient();
        _trustedSignerCertificates = [.. options.TrustedSignerCertificates];
        _logger = logger;

        if (_trustedSignerCertificates.Length != 0)
        {
            var candidates = new X509Certificate2Collection();
            candidates.AddRange(_trustedSignerCertificates);
            _extraCandidates = candidates;
        }
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpointSignature> SignAsync(
        AuditCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var hashBytes = ParseCheckpointHash(checkpoint);

        Rfc3161TimestampRequest timestampRequest;
        byte[] requestBytes;

        try
        {
            timestampRequest = Rfc3161TimestampRequest.CreateFromHash(
                hashBytes,
                _options.HashAlgorithm,
                null,
                RandomNumberGenerator.GetBytes(NonceSizeInBytes),
                _options.RequestSignerCertificates,
                null);

            requestBytes = timestampRequest.Encode();
        }
        catch (CryptographicException exception)
        {
            throw TimestampFailed(
                checkpoint,
                "the timestamp query for its hash cannot be built",
                exception);
        }

        var responseBytes = await PostQueryAsync(checkpoint, requestBytes, cancellationToken).ConfigureAwait(false);

        Rfc3161TimestampToken token;

        try
        {
            token = timestampRequest.ProcessResponse(new ReadOnlyMemory<byte>(responseBytes), out _);
        }
        catch (CryptographicException exception)
        {
            throw TimestampFailed(
                checkpoint,
                "the authority refused the query or returned a response that does not match it",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw TimestampFailed(
                checkpoint,
                "the authority returned a response that cannot be read as a timestamp response",
                exception);
        }
        catch (FormatException exception)
        {
            throw TimestampFailed(
                checkpoint,
                "the authority returned a response that cannot be read as a timestamp response",
                exception);
        }

        var signature = new AuditCheckpointSignature(
            SignatureScheme,
            Convert.ToBase64String(token.AsSignedCms().Encode()));

        if (_logger is not null)
        {
            Rfc3161TimestampLogMessages.CheckpointTimestamped(
                _logger,
                checkpoint.Sequence,
                checkpoint.Segment,
                _options.Authority.ToString(),
                token.TokenInfo.Timestamp);
        }

        return signature;
    }

    /// <inheritdoc />
    public ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var signature = FindTimestampSignature(checkpoint);

        if (signature is null)
        {
            if (_logger is not null)
            {
                Rfc3161TimestampLogMessages.TimestampSignatureMissing(
                    _logger,
                    checkpoint.Sequence,
                    checkpoint.Segment);
            }

            return ValueTask.FromResult(false);
        }

        byte[] tokenBytes;

        try
        {
            tokenBytes = Convert.FromBase64String(signature.Value);
        }
        catch (FormatException)
        {
            ReportInvalid(checkpoint, "its value is not Base64");

            return ValueTask.FromResult(false);
        }

        if (!Rfc3161TimestampToken.TryDecode(new ReadOnlyMemory<byte>(tokenBytes), out var token, out _)
            || token is null)
        {
            ReportInvalid(checkpoint, "its value is not a time-stamp token");

            return ValueTask.FromResult(false);
        }

        byte[] hashBytes;

        try
        {
            hashBytes = Convert.FromHexString(checkpoint.Hash);
        }
        catch (FormatException)
        {
            ReportInvalid(checkpoint, "the checkpoint hash is not hexadecimal");

            return ValueTask.FromResult(false);
        }

        if (hashBytes.Length == 0)
        {
            ReportInvalid(checkpoint, "the checkpoint carries no hash");

            return ValueTask.FromResult(false);
        }

        bool coversHash;

        try
        {
            coversHash = token.VerifySignatureForHash(
                hashBytes,
                _options.HashAlgorithm,
                out var signerCertificate,
                _extraCandidates);

            if (coversHash && _trustedSignerCertificates.Length != 0)
            {
                coversHash = signerCertificate is not null && IsTrustedSigner(signerCertificate);
            }
        }
        catch (CryptographicException)
        {
            ReportInvalid(checkpoint, "the token signature cannot be checked");

            return ValueTask.FromResult(false);
        }
        catch (InvalidOperationException)
        {
            ReportInvalid(checkpoint, "the token signature cannot be checked");

            return ValueTask.FromResult(false);
        }
        catch (FormatException)
        {
            ReportInvalid(checkpoint, "the token signature cannot be checked");

            return ValueTask.FromResult(false);
        }

        if (!coversHash)
        {
            ReportInvalid(
                checkpoint,
                _trustedSignerCertificates.Length == 0
                    ? "the token does not cover the checkpoint hash"
                    : "the token does not cover the checkpoint hash or its signer is not trusted");

            return ValueTask.FromResult(false);
        }

        if (_logger is not null)
        {
            Rfc3161TimestampLogMessages.CheckpointTimestampVerified(
                _logger,
                checkpoint.Sequence,
                checkpoint.Segment);
        }

        return ValueTask.FromResult(true);
    }

    /// <summary>
    ///     Posts a timestamp query to the authority and reads the raw response bytes.
    /// </summary>
    /// <param name="checkpoint">The checkpoint the query time-stamps, named when the exchange fails.</param>
    /// <param name="requestBytes">The DER-encoded timestamp query.</param>
    /// <param name="cancellationToken">The token used to cancel the exchange.</param>
    /// <returns>The raw timestamp response bytes.</returns>
    private async Task<byte[]> PostQueryAsync(
        AuditCheckpoint checkpoint,
        byte[] requestBytes,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_options.Timeout);

        using var content = new ByteArrayContent(requestBytes);
        _ = content.Headers.TryAddWithoutValidation("Content-Type", TimestampQueryMediaType);

        try
        {
            using var response = await _httpClient
                .PostAsync(_options.Authority, content, timeoutSource.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw TimestampFailed(
                    checkpoint,
                    $"the authority answered with {(int) response.StatusCode} ({response.ReasonPhrase})");
            }

            return await response.Content
                .ReadAsByteArrayAsync(timeoutSource.Token)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();

            throw TimestampFailed(
                checkpoint,
                $"the authority did not answer within {_options.Timeout}",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw TimestampFailed(checkpoint, "the authority cannot be reached", exception);
        }
    }

    /// <summary>
    ///     Finds the timestamp signature on a checkpoint, in either signature slot.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to search.</param>
    /// <returns>
    ///     The first <see cref="SignatureScheme" /> signature, or <see langword="null" /> when the checkpoint
    ///     carries none.
    /// </returns>
    private static AuditCheckpointSignature? FindTimestampSignature(AuditCheckpoint checkpoint)
    {
        foreach (var signature in checkpoint.Signatures)
        {
            if (signature is not null
                && string.Equals(signature.Scheme, SignatureScheme, StringComparison.Ordinal))
            {
                return signature;
            }
        }

        if (checkpoint.Signature is not null
            && string.Equals(checkpoint.Signature.Scheme, SignatureScheme, StringComparison.Ordinal))
        {
            return checkpoint.Signature;
        }

        return null;
    }

    /// <summary>
    ///     Decides whether a token signer is one of the pinned authority certificates.
    /// </summary>
    /// <param name="signerCertificate">The certificate that signed the token.</param>
    /// <returns>
    ///     <see langword="true" /> when the signer matches a pinned certificate byte for byte; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    private bool IsTrustedSigner(X509Certificate2 signerCertificate)
    {
        foreach (var pinned in _trustedSignerCertificates)
        {
            if (signerCertificate.RawData.AsSpan().SequenceEqual(pinned.RawData))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Reads the checkpoint hash as the digest bytes the authority time-stamps.
    /// </summary>
    /// <param name="checkpoint">The checkpoint whose hash is read.</param>
    /// <returns>The checkpoint hash bytes.</returns>
    private static byte[] ParseCheckpointHash(AuditCheckpoint checkpoint)
    {
        if (checkpoint.Hash.Length == 0)
        {
            throw TimestampFailed(checkpoint, "it carries no hash");
        }

        try
        {
            return Convert.FromHexString(checkpoint.Hash);
        }
        catch (FormatException exception)
        {
            throw TimestampFailed(checkpoint, "its hash is not hexadecimal", exception);
        }
    }

    /// <summary>
    ///     Reports an unusable timestamp signature to the logger, when one is configured.
    /// </summary>
    /// <param name="checkpoint">The checkpoint that carries the signature.</param>
    /// <param name="reason">Why the signature cannot prove the checkpoint hash.</param>
    private void ReportInvalid(AuditCheckpoint checkpoint, string reason)
    {
        if (_logger is not null)
        {
            Rfc3161TimestampLogMessages.TimestampSignatureInvalid(
                _logger,
                checkpoint.Sequence,
                checkpoint.Segment,
                reason);
        }
    }

    /// <summary>
    ///     Builds the exception a failed timestamp exchange throws.
    /// </summary>
    /// <param name="checkpoint">The checkpoint that could not be time-stamped.</param>
    /// <param name="reason">Why the exchange failed.</param>
    /// <returns>The exception describing the failed exchange.</returns>
    private static Rfc3161TimestampException TimestampFailed(AuditCheckpoint checkpoint, string reason)
    {
        return new Rfc3161TimestampException(
            $"Checkpoint {checkpoint.Sequence} of segment '{checkpoint.Segment}' cannot be time-stamped: {reason}.");
    }

    /// <summary>
    ///     Builds the exception a failed timestamp exchange throws, keeping the transport or protocol cause.
    /// </summary>
    /// <param name="checkpoint">The checkpoint that could not be time-stamped.</param>
    /// <param name="reason">Why the exchange failed.</param>
    /// <param name="innerException">The transport or protocol exception the exchange failed with.</param>
    /// <returns>The exception describing the failed exchange.</returns>
    private static Rfc3161TimestampException TimestampFailed(
        AuditCheckpoint checkpoint,
        string reason,
        Exception innerException)
    {
        return new Rfc3161TimestampException(
            $"Checkpoint {checkpoint.Sequence} of segment '{checkpoint.Segment}' cannot be time-stamped: {reason}.",
            innerException);
    }
}
