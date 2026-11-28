using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Signs audit checkpoints and verifies their signatures.
/// </summary>
/// <remarks>
///     The signer owns the key and the scheme: a keyed MAC, an asymmetric key, or an external timestamping authority.
/// </remarks>
public interface ICheckpointSigner
{
    /// <summary>
    ///     Signs a checkpoint.
    /// </summary>
    /// <param name="checkpoint">The checkpoint without a signature.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The signature to attach.</returns>
    ValueTask<AuditCheckpointSignature> SignAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    ///     Verifies the signature a checkpoint carries.
    /// </summary>
    /// <param name="checkpoint">The checkpoint with its signature.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns><see langword="true" /> when the signature is present and valid.</returns>
    ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken);
}
