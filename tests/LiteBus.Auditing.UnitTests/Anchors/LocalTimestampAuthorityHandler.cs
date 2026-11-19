using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace LiteBus.Auditing.UnitTests.Anchors;

/// <summary>
///     An in-memory RFC 3161 time-stamping authority that answers timestamp queries without the network.
/// </summary>
/// <remarks>
///     The handler decodes each query, signs its imprint with the test certificate, and returns a DER-encoded
///     timestamp response, so signer tests exercise the real request, transport, and response path. It records
///     every query it receives for assertions.
/// </remarks>
internal sealed class LocalTimestampAuthorityHandler : HttpMessageHandler
{
    /// <summary>
    ///     The media type timestamp queries are posted with.
    /// </summary>
    private const string TimestampQueryMediaType = "application/timestamp-query";

    /// <summary>
    ///     The media type timestamp responses are answered with.
    /// </summary>
    private const string TimestampReplyMediaType = "application/timestamp-reply";

    /// <summary>
    ///     The content type identifying a time-stamp token inside its message.
    /// </summary>
    private const string TimestampTokenContentType = "1.2.840.113549.1.9.16.1.4";

    /// <summary>
    ///     The hash algorithm identifying digests time-stamped in tests.
    /// </summary>
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>
    ///     The test policy tokens are issued under.
    /// </summary>
    private const string TestPolicyOid = "1.2.3.4.5.6";

    /// <summary>
    ///     The signed attribute identifying the signing certificate, which RFC 3161 tokens must carry.
    /// </summary>
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    /// <summary>
    ///     The certificate tokens are signed with. Held for the life of the handler.
    /// </summary>
    private readonly X509Certificate2 _signingCertificate;

    /// <summary>
    ///     How queries are answered. Fixed for the life of the handler.
    /// </summary>
    private readonly LocalTimestampAuthorityMode _mode;

    /// <summary>
    ///     The status error answers carry. Fixed for the life of the handler.
    /// </summary>
    private readonly HttpStatusCode _errorStatus;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LocalTimestampAuthorityHandler" /> class.
    /// </summary>
    /// <param name="signingCertificate">The certificate tokens are signed with.</param>
    /// <param name="mode">How queries are answered.</param>
    /// <param name="errorStatus">The status error answers carry.</param>
    public LocalTimestampAuthorityHandler(
        X509Certificate2 signingCertificate,
        LocalTimestampAuthorityMode mode = LocalTimestampAuthorityMode.Success,
        HttpStatusCode errorStatus = HttpStatusCode.InternalServerError)
    {
        ArgumentNullException.ThrowIfNull(signingCertificate);

        _signingCertificate = signingCertificate;
        _mode = mode;
        _errorStatus = errorStatus;
    }

    /// <summary>
    ///     Gets how many queries the authority received.
    /// </summary>
    public int RequestCount { get; private set; }

    /// <summary>
    ///     Gets the media type of the last query received, or <see langword="null" /> when no query arrived
    ///     or it carried none.
    /// </summary>
    public string? LastContentType { get; private set; }

    /// <summary>
    ///     Gets the last decoded timestamp query, or <see langword="null" /> when no query was decoded.
    /// </summary>
    public Rfc3161TimestampRequest? LastRequest { get; private set; }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestCount++;
        LastContentType = request.Content?.Headers.ContentType?.ToString();

        if (_mode == LocalTimestampAuthorityMode.NeverRespond)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                "The local timestamp authority was told never to respond, but the wait ended.");
        }

        if (_mode == LocalTimestampAuthorityMode.Unreachable)
        {
            throw new HttpRequestException("The local timestamp authority cannot be reached.");
        }

        if (_mode == LocalTimestampAuthorityMode.HttpError)
        {
            return new HttpResponseMessage(_errorStatus);
        }

        if (request.Content is null)
        {
            throw new InvalidOperationException(
                "The local timestamp authority received a query without a body.");
        }

        var requestBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        if (!Rfc3161TimestampRequest.TryDecode(requestBytes, out var timestampRequest, out _)
            || timestampRequest is null)
        {
            throw new InvalidOperationException(
                "The local timestamp authority received bytes that are not an RFC 3161 timestamp request.");
        }

        LastRequest = timestampRequest;

        var responseBytes = _mode == LocalTimestampAuthorityMode.FailureStatus
            ? EncodeFailure()
            : EncodeSuccess(timestampRequest);

        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new ByteArrayContent(responseBytes);
        _ = response.Content.Headers.TryAddWithoutValidation("Content-Type", TimestampReplyMediaType);

        return response;
    }

    /// <summary>
    ///     Signs a query imprint and wraps the token in a granted timestamp response.
    /// </summary>
    /// <param name="timestampRequest">The decoded timestamp query.</param>
    /// <returns>The DER-encoded timestamp response.</returns>
    private byte[] EncodeSuccess(Rfc3161TimestampRequest timestampRequest)
    {
        var tokenInfo = new Rfc3161TimestampTokenInfo(
            new Oid(TestPolicyOid),
            new Oid(Sha256Oid),
            timestampRequest.GetMessageHash(),
            new ReadOnlyMemory<byte>([1]),
            DateTimeOffset.UtcNow,
            null,
            false,
            timestampRequest.GetNonce(),
            null,
            null);

        var content = new ContentInfo(new Oid(TimestampTokenContentType), tokenInfo.Encode());
        var token = new SignedCms(content, detached: false);
        var signer = new CmsSigner(_signingCertificate)
        {
            DigestAlgorithm = new Oid(Sha256Oid),
            IncludeOption = X509IncludeOption.WholeChain,
        };
        _ = signer.SignedAttributes.Add(CreateSigningCertificateAttribute());
        token.ComputeSignature(signer);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteInteger(0);
        writer.PopSequence();
        writer.WriteEncodedValue(token.Encode());
        writer.PopSequence();

        return writer.Encode();
    }

    /// <summary>
    ///     Builds the signed attribute identifying the signing certificate.
    /// </summary>
    /// <returns>The signing certificate attribute for the test certificate.</returns>
    private CryptographicAttributeObject CreateSigningCertificateAttribute()
    {
        var certHash = SHA256.HashData(_signingCertificate.RawData);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteObjectIdentifier(Sha256Oid);
        writer.WriteNull();
        writer.PopSequence();
        writer.WriteOctetString(certHash);
        writer.PopSequence();
        writer.PopSequence();
        writer.PopSequence();

        var attributeOid = new Oid(SigningCertificateV2Oid);
        var values = new AsnEncodedDataCollection();
        _ = values.Add(new AsnEncodedData(attributeOid, writer.Encode()));

        return new CryptographicAttributeObject(attributeOid, values);
    }

    /// <summary>
    ///     Builds a timestamp response with a rejection status and no token.
    /// </summary>
    /// <returns>The DER-encoded timestamp response.</returns>
    private static byte[] EncodeFailure()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.PushSequence();
        writer.PushSequence();
        writer.WriteInteger(2);
        writer.PopSequence();
        writer.PopSequence();

        return writer.Encode();
    }
}
