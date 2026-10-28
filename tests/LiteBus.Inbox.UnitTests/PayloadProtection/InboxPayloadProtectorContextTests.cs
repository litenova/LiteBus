using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests.PayloadProtection;

/// <summary>
///     Verifies the inbox protector forwards contextual encryption and normalizes unscoped tenants.
/// </summary>
public sealed class InboxPayloadProtectorContextTests
{
    /// <summary>
    ///     Verifies contextual encryption round-trips through the inbox protector.
    /// </summary>
    [Fact]
    public async Task RoundTrip_WithSameContext_Succeeds()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var context = CreateContext("tenant-a");

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, context).ConfigureAwait(false);
        var plaintext = await global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, context).ConfigureAwait(false);

        plaintext.Should().Be("secret");
        inner.ContextualEncryptCalled.Should().BeTrue();
        inner.ContextualDecryptCalled.Should().BeTrue();
        inner.PlainEncryptCalled.Should().BeFalse();
        inner.PlainDecryptCalled.Should().BeFalse();
    }

    /// <summary>
    ///     Verifies moving ciphertext to another tenant fails decryption.
    /// </summary>
    [Fact]
    public async Task Decrypt_WithDifferentTenant_ThrowsCryptographicException()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext("tenant-a");
        var decryptContext = CreateContext("tenant-b") with { MessageId = encryptContext.MessageId };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var decrypt = () => global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext);

        await decrypt.Should().ThrowAsync<CryptographicException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies moving ciphertext to another message identifier fails decryption.
    /// </summary>
    [Fact]
    public async Task Decrypt_WithDifferentMessageId_ThrowsCryptographicException()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext("tenant-a");
        var decryptContext = encryptContext with { MessageId = Guid.NewGuid() };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var decrypt = () => global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext);

        await decrypt.Should().ThrowAsync<CryptographicException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies moving ciphertext to another contract name fails decryption.
    /// </summary>
    [Fact]
    public async Task Decrypt_WithDifferentContractName_ThrowsCryptographicException()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext("tenant-a");
        var decryptContext = encryptContext with { ContractName = "orders.commands.cancel" };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var decrypt = () => global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext);

        await decrypt.Should().ThrowAsync<CryptographicException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies moving ciphertext to another contract version fails decryption.
    /// </summary>
    [Fact]
    public async Task Decrypt_WithDifferentContractVersion_ThrowsCryptographicException()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext("tenant-a");
        var decryptContext = encryptContext with { ContractVersion = encryptContext.ContractVersion + 1 };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var decrypt = () => global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext);

        await decrypt.Should().ThrowAsync<CryptographicException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies moving ciphertext to another durable axis fails decryption.
    /// </summary>
    [Fact]
    public async Task Decrypt_WithDifferentAxis_ThrowsCryptographicException()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext("tenant-a");
        var decryptContext = encryptContext with { Axis = "outbox" };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var decrypt = () => global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext);

        await decrypt.Should().ThrowAsync<CryptographicException>().ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies unscoped messages round-trip when encrypt uses null and decrypt uses empty tenant.
    /// </summary>
    [Fact]
    public async Task Unscoped_NullEncryptAndEmptyDecrypt_RoundTrips()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext(null);
        var decryptContext = encryptContext with { TenantId = string.Empty };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var plaintext = await global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext).ConfigureAwait(false);

        plaintext.Should().Be("secret");
    }

    /// <summary>
    ///     Verifies unscoped messages round-trip when encrypt uses empty and decrypt uses null tenant.
    /// </summary>
    [Fact]
    public async Task Unscoped_EmptyEncryptAndNullDecrypt_RoundTrips()
    {
        var inner = new ContextBindingEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var encryptContext = CreateContext(string.Empty);
        var decryptContext = encryptContext with { TenantId = null };

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, encryptContext).ConfigureAwait(false);
        var plaintext = await global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, decryptContext).ConfigureAwait(false);

        plaintext.Should().Be("secret");
    }

    /// <summary>
    ///     Verifies the protector falls back to plain methods for non-contextual encryptors.
    /// </summary>
    [Fact]
    public async Task Protect_WithPlainEncryptor_FallsBackToPlainContract()
    {
        var inner = new PlainEncryptor();
        IInboxPayloadProtector protector = new InboxPayloadProtector(inner);
        var context = CreateContext("tenant-a");

        var ciphertext = await global::LiteBus.Messaging.PayloadProtection.ProtectAsync("secret", protector, context).ConfigureAwait(false);
        var plaintext = await global::LiteBus.Messaging.PayloadProtection.UnprotectAsync(ciphertext, protector, context).ConfigureAwait(false);

        plaintext.Should().Be("secret");
        inner.EncryptCalled.Should().BeTrue();
        inner.DecryptCalled.Should().BeTrue();
    }

    /// <summary>
    ///     Creates a protection context for the inbox axis.
    /// </summary>
    /// <param name="tenantId">The tenant identifier, or null for unscoped messages.</param>
    /// <returns>The protection context.</returns>
    private static PayloadProtectionContext CreateContext(string? tenantId)
    {
        return new PayloadProtectionContext
        {
            MessageId = Guid.NewGuid(),
            ContractName = "orders.commands.ship",
            ContractVersion = 1,
            TenantId = tenantId,
            Axis = "inbox"
        };
    }

    /// <summary>
    ///     Fake encryptor that binds the protection context to ciphertext.
    /// </summary>
    private sealed class ContextBindingEncryptor : IContextualPayloadEncryptor
    {
        /// <summary>
        ///     Gets the field separator used in the test ciphertext format.
        /// </summary>
        private const char Separator = '|';

        /// <summary>
        ///     Gets the token that represents a null tenant in the test ciphertext format.
        /// </summary>
        private const string NullTenantToken = "<null>";

        /// <summary>
        ///     Gets a value indicating whether the contextual encrypt overload was called.
        /// </summary>
        internal bool ContextualEncryptCalled { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the contextual decrypt overload was called.
        /// </summary>
        internal bool ContextualDecryptCalled { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the plain encrypt overload was called.
        /// </summary>
        internal bool PlainEncryptCalled { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the plain decrypt overload was called.
        /// </summary>
        internal bool PlainDecryptCalled { get; private set; }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            PlainEncryptCalled = true;
            return Task.FromResult("plain:" + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            PlainDecryptCalled = true;
            return Task.FromResult(
                ciphertext.StartsWith("plain:", StringComparison.Ordinal) ? ciphertext.Substring(6) : ciphertext);
        }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, PayloadProtectionContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(plaintext);
            ArgumentNullException.ThrowIfNull(context);

            ContextualEncryptCalled = true;
            var tenantToken = context.TenantId ?? NullTenantToken;
            var ciphertext = string.Join(
                Separator,
                [
                    context.MessageId.ToString("D"),
                    context.ContractName,
                    context.ContractVersion.ToString(CultureInfo.InvariantCulture),
                    tenantToken,
                    context.Axis,
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext))
                ]);

            return Task.FromResult(ciphertext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, PayloadProtectionContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ciphertext);
            ArgumentNullException.ThrowIfNull(context);

            ContextualDecryptCalled = true;
            var parts = ciphertext.Split(Separator);

            if (parts.Length != 6)
            {
                throw new CryptographicException("Ciphertext is not in the expected test format.");
            }

            var boundMessageId = Guid.Parse(parts[0]);
            var boundContractName = parts[1];
            var boundContractVersion = int.Parse(parts[2], CultureInfo.InvariantCulture);
            var boundTenant = parts[3] == NullTenantToken ? null : parts[3];
            var boundAxis = parts[4];

            if (boundMessageId != context.MessageId ||
                !string.Equals(boundContractName, context.ContractName, StringComparison.Ordinal) ||
                boundContractVersion != context.ContractVersion ||
                !string.Equals(boundTenant, context.TenantId, StringComparison.Ordinal) ||
                !string.Equals(boundAxis, context.Axis, StringComparison.Ordinal))
            {
                throw new CryptographicException("Protection context does not match the bound values.");
            }

            return Task.FromResult(Encoding.UTF8.GetString(Convert.FromBase64String(parts[5])));
        }
    }

    /// <summary>
    ///     Fake encryptor without contextual support.
    /// </summary>
    private sealed class PlainEncryptor : IPayloadEncryptor
    {
        /// <summary>
        ///     Gets a value indicating whether encrypt was called.
        /// </summary>
        internal bool EncryptCalled { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether decrypt was called.
        /// </summary>
        internal bool DecryptCalled { get; private set; }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            EncryptCalled = true;
            return Task.FromResult("plain:" + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            DecryptCalled = true;
            return Task.FromResult(
                ciphertext.StartsWith("plain:", StringComparison.Ordinal) ? ciphertext.Substring(6) : ciphertext);
        }
    }
}
