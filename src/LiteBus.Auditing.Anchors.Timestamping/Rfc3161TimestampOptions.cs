using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LiteBus.Auditing.Anchors.Timestamping;

/// <summary>
///     The time-stamping authority and token settings an <see cref="Rfc3161CheckpointSigner" /> signs checkpoints with.
/// </summary>
/// <remarks>
///     <para>
///         The authority is the only required value: every other setting has a default that suits an authority
///         speaking RFC 3161 over HTTPS. The hash algorithm labels the checkpoint digest inside the timestamp
///         request; it must stay the same between signing and verification, because verification asks the token
///         whether it covers the checkpoint digest under that algorithm.
///     </para>
///     <para>
///         Verification without <see cref="TrustedSignerCertificates" /> checks that the token signature is valid
///         and covers the checkpoint digest, but does not decide which authorities are trustworthy. Pinning the
///         authority certificate makes verification report a token signed by anyone else as invalid.
///     </para>
/// </remarks>
public sealed record Rfc3161TimestampOptions
{
    /// <summary>
    ///     Gets how long a timestamp request waits for the authority unless configured otherwise.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Initializes a new instance of the <see cref="Rfc3161TimestampOptions" /> class.
    /// </summary>
    /// <param name="authority">The time-stamping authority timestamp queries are posted to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authority" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="authority" /> is not an absolute HTTP or HTTPS URI.
    /// </exception>
    public Rfc3161TimestampOptions(Uri authority)
    {
        ValidateAuthority(authority);

        Authority = authority;
    }

    /// <summary>
    ///     Gets the time-stamping authority timestamp queries are posted to.
    /// </summary>
    public Uri Authority { get; }

    /// <summary>
    ///     Gets the hash algorithm naming the checkpoint digest inside timestamp requests.
    /// </summary>
    /// <value>
    ///     The algorithm label carried in the request imprint. Defaults to SHA-256.
    /// </value>
    public HashAlgorithmName HashAlgorithm { get; init; } = HashAlgorithmName.SHA256;

    /// <summary>
    ///     Gets a value indicating whether the authority must include its signing certificate in issued tokens.
    /// </summary>
    /// <value>
    ///     <see langword="true" /> unless configured otherwise. Turn this off only for authorities whose policy
    ///     forbids it; verification then needs the authority certificate in
    ///     <see cref="TrustedSignerCertificates" />.
    /// </value>
    public bool RequestSignerCertificates { get; init; } = true;

    /// <summary>
    ///     Gets how long a timestamp request waits for the authority.
    /// </summary>
    /// <value>
    ///     The per-request timeout. Defaults to <see cref="DefaultTimeout" />. Must be positive.
    /// </value>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>
    ///     Gets the authority certificates verification trusts, or an empty list when any signer whose signature
    ///     is valid is accepted.
    /// </summary>
    /// <value>
    ///     The pinned authority certificates, compared byte for byte against the token signer. Empty by default.
    /// </value>
    public IReadOnlyList<X509Certificate2> TrustedSignerCertificates { get; init; } = [];

    /// <summary>
    ///     Rejects a timestamp authority URI that the signer cannot post timestamp queries to.
    /// </summary>
    /// <param name="authority">The candidate authority URI.</param>
    /// <exception cref="ArgumentNullException"><paramref name="authority" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="authority" /> is not an absolute HTTP or HTTPS URI.
    /// </exception>
    internal static void ValidateAuthority(Uri? authority)
    {
        ArgumentNullException.ThrowIfNull(authority);

        if (!authority.IsAbsoluteUri
            || (authority.Scheme != Uri.UriSchemeHttp && authority.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "The timestamp authority must be an absolute HTTP or HTTPS URI.",
                nameof(authority));
        }
    }
}
