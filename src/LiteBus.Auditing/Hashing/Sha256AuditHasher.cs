using System;
using LiteBus.Auditing.Abstractions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Hashes audit chain bytes with plain SHA-256.
/// </summary>
/// <remarks>
///     <para>
///         The default <see cref="IAuditHasher" />: a deterministic digest with no key to provision, rotate, or fetch,
///         so a chain that needs tamper evidence but no keyed attribution writes no hashing code. An application that
///         must bind its hashes to a secret uses <see cref="HmacSha256AuditHasher" /> instead.
///     </para>
///     <para>
///         The hash is the lowercase hexadecimal SHA-256 of the linked bytes, the same encoding the stored
///         <see cref="IAuditChainDocument.Hash" /> uses.
///     </para>
/// </remarks>
public sealed class Sha256AuditHasher : IAuditHasher
{
    /// <summary>
    ///     The algorithm part of a scheme name built on this hasher, such as the <c>sha256</c> in
    ///     <c>sha256-v2</c>.
    /// </summary>
    public const string AlgorithmName = "sha256";

    /// <inheritdoc />
    public ValueTask<string> HashAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Convert.ToHexStringLower(SHA256.HashData(data.Span)));
    }
}
