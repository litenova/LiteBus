using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Extends payload encryption with authenticated durable metadata.
/// </summary>
/// <remarks>
///     Implementations should use the canonical context as associated data so ciphertext cannot be moved between
///     message rows, contracts, tenants, or durable axes without authentication failure.
/// </remarks>
public interface IContextualPayloadEncryptor : IPayloadEncryptor
{
    /// <summary>
    ///     Encrypts a payload while authenticating its durable metadata.
    /// </summary>
    /// <param name="plaintext">The serialized payload.</param>
    /// <param name="context">The immutable metadata bound to ciphertext.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The encrypted payload text.</returns>
    Task<string> EncryptAsync(
        string plaintext,
        PayloadProtectionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts a payload after authenticating its durable metadata.
    /// </summary>
    /// <param name="ciphertext">The encrypted payload text.</param>
    /// <param name="context">The immutable metadata bound to ciphertext.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The decrypted serialized payload.</returns>
    Task<string> DecryptAsync(
        string ciphertext,
        PayloadProtectionContext context,
        CancellationToken cancellationToken = default);
}
