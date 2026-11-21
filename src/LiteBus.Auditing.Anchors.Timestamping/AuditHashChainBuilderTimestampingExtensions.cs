using System;
using LiteBus.Auditing;

namespace LiteBus.Auditing.Anchors.Timestamping;

/// <summary>
///     Registers RFC 3161 timestamp checkpoint signing through <see cref="AuditHashChainBuilder" />.
/// </summary>
public static class AuditHashChainBuilderTimestampingExtensions
{
    /// <summary>
    ///     Signs every cut checkpoint with a time-stamp token from the given authority.
    /// </summary>
    /// <param name="builder">The audit hash chain builder.</param>
    /// <param name="authority">
    ///     The time-stamping authority the signer posts timestamp queries to. Must be an absolute HTTP or
    ///     HTTPS URI.
    /// </param>
    /// <returns>The audit hash chain builder for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder" /> or <paramref name="authority" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="authority" /> is not an absolute HTTP or HTTPS URI.
    /// </exception>
    /// <remarks>
    ///     Registers <see cref="Rfc3161CheckpointSigner" />, which reads the authority from
    ///     <see cref="Rfc3161TimestampOptions" /> and returns the issued token as the checkpoint signature, so
    ///     the timestamp sits beside the log signature and witness cosignatures. Registering twice adds the
    ///     signer once.
    /// </remarks>
    public static AuditHashChainBuilder TimestampCheckpoints(
        this AuditHashChainBuilder builder,
        Uri authority)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Rfc3161TimestampOptions.ValidateAuthority(authority);

        return builder.SignCheckpointsWith<Rfc3161CheckpointSigner>();
    }
}
