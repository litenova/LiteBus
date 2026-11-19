using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions.Auditing;

/// <summary>
///     Computes the hash of linked audit bytes.
/// </summary>
/// <remarks>
///     The hasher owns the algorithm and the key: a plain digest, a keyed MAC with a key from a secret store, or a
///     hardware-backed signer. It is asynchronous so a key can be fetched or a remote signer called.
/// </remarks>
public interface IAuditHasher
{
    /// <summary>
    ///     Hashes the given bytes.
    /// </summary>
    /// <param name="data">The linked bytes of one document.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The hash as a string, stored on the document.</returns>
    ValueTask<string> HashAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}
