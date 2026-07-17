using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging;

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

    /// <summary>
    ///     Encrypts a payload with optional authenticated metadata.
    /// </summary>
    /// <param name="payload">The serialized payload.</param>
    /// <param name="encryptor">The optional encryptor.</param>
    /// <param name="context">The metadata authenticated by contextual encryptors.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The protected payload text.</returns>
    public static Task<string> ProtectAsync(
        string payload,
        IPayloadEncryptor? encryptor,
        PayloadProtectionContext? context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ProtectCoreAsync(payload, encryptor, context, cancellationToken);
    }

    /// <summary>
    ///     Decrypts a payload with optional authenticated metadata.
    /// </summary>
    /// <param name="payload">The stored payload text.</param>
    /// <param name="encryptor">The optional encryptor.</param>
    /// <param name="context">The metadata authenticated by contextual encryptors.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The decrypted serialized payload.</returns>
    public static Task<string> UnprotectAsync(
        string payload,
        IPayloadEncryptor? encryptor,
        PayloadProtectionContext? context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return UnprotectCoreAsync(payload, encryptor, context, cancellationToken);
    }

    /// <summary>
    ///     Applies the configured encryptor and passes contextual metadata when supported.
    /// </summary>
    /// <param name="payload">The serialized payload.</param>
    /// <param name="encryptor">The optional encryptor.</param>
    /// <param name="context">The metadata authenticated by contextual encryptors.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The protected payload text.</returns>
    private static async Task<string> ProtectCoreAsync(
        string payload,
        IPayloadEncryptor? encryptor,
        PayloadProtectionContext? context,
        CancellationToken cancellationToken)
    {
        if (encryptor is null)
        {
            return payload;
        }

        var encrypted = context is not null && encryptor is IContextualPayloadEncryptor contextualEncryptor
            ? await contextualEncryptor.EncryptAsync(payload, context, cancellationToken).ConfigureAwait(false)
            : await encryptor.EncryptAsync(payload, cancellationToken).ConfigureAwait(false);

        return encrypted;
    }

    /// <summary>
    ///     Invokes the configured decryptor with contextual metadata when supported.
    /// </summary>
    /// <param name="payload">The stored payload text.</param>
    /// <param name="encryptor">The optional encryptor.</param>
    /// <param name="context">The metadata authenticated by contextual encryptors.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The decrypted serialized payload.</returns>
    private static async Task<string> UnprotectCoreAsync(
        string payload,
        IPayloadEncryptor? encryptor,
        PayloadProtectionContext? context,
        CancellationToken cancellationToken)
    {
        if (encryptor is null)
        {
            return payload;
        }

        return context is not null && encryptor is IContextualPayloadEncryptor contextualEncryptor
            ? await contextualEncryptor.DecryptAsync(payload, context, cancellationToken).ConfigureAwait(false)
            : await encryptor.DecryptAsync(payload, cancellationToken).ConfigureAwait(false);
    }
}
