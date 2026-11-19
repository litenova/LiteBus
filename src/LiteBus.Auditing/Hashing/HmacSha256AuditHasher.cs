using System;
using LiteBus.Auditing.Abstractions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Hashing;

/// <summary>
///     Hashes audit chain bytes with HMAC-SHA-256 under an application key.
/// </summary>
/// <remarks>
///     <para>
///         The keyed <see cref="IAuditHasher" />: anyone holding the stored hashes but not the key can still verify the
///         links, but cannot forge a record that verifies. The key is either fixed at construction or supplied per
///         operation by a provider, so an application that reads its key from a secret store rotates it without
///         restarting the host.
///     </para>
///     <para>
///         The hash is the lowercase hexadecimal HMAC-SHA-256 of the linked bytes, the same encoding the stored
///         <see cref="IAuditChainDocument.Hash" /> uses.
///     </para>
/// </remarks>
public sealed class HmacSha256AuditHasher : IAuditHasher
{
    /// <summary>
    ///     The algorithm part of a scheme name built on this hasher, such as the <c>hmac-sha256</c> in
    ///     <c>hmac-sha256-v2</c>.
    /// </summary>
    public const string AlgorithmName = "hmac-sha256";

    /// <summary>
    ///     The key bytes copied at construction, when the hasher was given a fixed key. Never mutated afterwards.
    /// </summary>
    private readonly byte[]? _key;

    /// <summary>
    ///     The provider that supplies the key per hash operation, when the hasher was given one. Held for the life of
    ///     the hasher.
    /// </summary>
    private readonly Func<CancellationToken, ValueTask<byte[]>>? _keyProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HmacSha256AuditHasher" /> class with a fixed key.
    /// </summary>
    /// <param name="key">The HMAC key. Copied, so the caller may reuse or clear its buffer afterwards.</param>
    /// <exception cref="ArgumentException"><paramref name="key" /> is empty.</exception>
    public HmacSha256AuditHasher(ReadOnlyMemory<byte> key)
    {
        if (key.IsEmpty)
        {
            throw new ArgumentException("The HMAC key must not be empty.", nameof(key));
        }

        _key = key.ToArray();
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="HmacSha256AuditHasher" /> class with a key provider.
    /// </summary>
    /// <param name="keyProvider">
    ///     The provider that returns the current key for each hash operation, so a rotation takes effect without a
    ///     restart. Must never return <see langword="null" /> or an empty key.
    /// </param>
    public HmacSha256AuditHasher(Func<CancellationToken, ValueTask<byte[]>> keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);

        _keyProvider = keyProvider;
    }

    /// <inheritdoc />
    public async ValueTask<string> HashAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        byte[] key;

        if (_keyProvider is not null)
        {
            var provided = await _keyProvider(cancellationToken).ConfigureAwait(false);

            if (provided is null || provided.Length == 0)
            {
                throw new InvalidOperationException("The audit HMAC key provider returned no key.");
            }

            key = provided;
        }
        else
        {
            key = _key!;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data.Span));
    }
}
