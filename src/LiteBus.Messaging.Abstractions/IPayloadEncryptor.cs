using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Encrypts and decrypts serialized message payloads before storage and after load.
/// </summary>
/// <remarks>
///     Contract name and version remain plaintext in inbox and outbox tables so routing and diagnostics continue to
///     work while payload bodies are protected at rest.
/// </remarks>
public interface IPayloadEncryptor
{
    /// <summary>
    ///     Encrypts a serialized payload before it is written to durable storage.
    /// </summary>
    /// <param name="plaintext">The serialized payload.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The encrypted payload text stored in the payload column.</returns>
    Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Decrypts a payload loaded from durable storage before deserialization.
    /// </summary>
    /// <param name="ciphertext">The encrypted payload text from storage.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The decrypted serialized payload.</returns>
    Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default);
}