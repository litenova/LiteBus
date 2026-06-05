using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Applies optional payload encryption around inbox, outbox, and dispatcher boundaries.
/// </summary>
public static class PayloadProtection
{
    /// <summary>
    ///     Encrypts a serialized payload when an encryptor is configured.
    /// </summary>
    /// <param name="payload">The serialized payload.</param>
    /// <param name="encryptor">The optional encryptor.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The protected payload text.</returns>
    public static Task<string> ProtectAsync(
        string payload,
        IPayloadEncryptor? encryptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return encryptor is null
            ? Task.FromResult(payload)
            : encryptor.EncryptAsync(payload, cancellationToken);
    }

    /// <summary>
    ///     Decrypts a stored payload when an encryptor is configured.
    /// </summary>
    /// <param name="payload">The stored payload text.</param>
    /// <param name="encryptor">The optional encryptor.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The decrypted serialized payload.</returns>
    public static Task<string> UnprotectAsync(
        string payload,
        IPayloadEncryptor? encryptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return encryptor is null
            ? Task.FromResult(payload)
            : encryptor.DecryptAsync(payload, cancellationToken);
    }
}
