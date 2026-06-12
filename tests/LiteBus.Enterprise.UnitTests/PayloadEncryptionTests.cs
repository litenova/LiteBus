using AwesomeAssertions;
using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Enterprise.UnitTests;

/// <summary>
///     Verifies payload encryption on accept and decrypt on dispatch.
/// </summary>
public sealed class PayloadEncryptionTests
{
    /// <summary>
    ///     Verifies stored payloads are encrypted and dispatch decrypts them.
    /// </summary>
    [Fact]
    public async Task Inbox_RoundTripsEncryptedPayloadThroughDispatch()
    {
        var store = new InMemoryInboxStore();
        IInboxPayloadProtector encryptor = new PrefixPayloadEncryptor("enc:");
        var registry = new MessageContractRegistry();
        registry.Register<TestCommand>("test-command");
        var serializer = new SystemTextJsonMessageSerializer();

        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System, encryptor));

        await inbox.AcceptAsync(InboxAcceptItem<TestCommand>.From(new TestCommand { Value = "secret" })).ConfigureAwait(false);

        var stored = store.GetAll().Single();
        stored.Payload.Should().StartWith("enc:");

        var dispatcher = new CapturingInboxDispatcher(serializer, encryptor);
        await dispatcher.DispatchAsync(stored).ConfigureAwait(false);

        CapturingInboxDispatcher.LastValue.Should().Be("secret");
    }

    /// <summary>
    ///     Verifies <see cref="CommandInboxDispatcher" /> decrypts payloads protected by <see cref="IInboxPayloadProtector" />.
    /// </summary>
    [Fact]
    public async Task CommandInboxDispatcher_DecryptsProtectedPayload()
    {
        var store = new InMemoryInboxStore();
        IInboxPayloadProtector protector = new PrefixInboxPayloadProtector("enc:");
        var registry = new MessageContractRegistry();
        registry.Register<TestCommand>("test-command");
        var serializer = new SystemTextJsonMessageSerializer();
        var mediator = new CapturingCommandMediator();

        var inbox = new Inbox.Inbox(
            store,
            new InboxEnvelopeFactory(registry, serializer, TimeProvider.System, protector));

        await inbox.AcceptAsync(InboxAcceptItem<TestCommand>.From(new TestCommand { Value = "secret" })).ConfigureAwait(false);

        var stored = store.GetAll().Single();
        var dispatcher = new CommandInboxDispatcher(mediator, registry, serializer, protector);
        await dispatcher.DispatchAsync(stored).ConfigureAwait(false);

        CapturingCommandMediator.LastValue.Should().Be("secret");
    }

    /// <summary>
    ///     Verifies <see cref="EventOutboxDispatcher" /> decrypts payloads protected by <see cref="IOutboxPayloadProtector" />.
    /// </summary>
    [Fact]
    public async Task EventOutboxDispatcher_DecryptsProtectedPayload()
    {
        IOutboxPayloadProtector protector = new PrefixOutboxPayloadProtector("enc:");
        var registry = new MessageContractRegistry();
        registry.Register<TestEvent>("test-event");
        var serializer = new SystemTextJsonMessageSerializer();
        var mediator = new CapturingEventMediator();
        var store = new InMemoryOutboxStore();

        var outbox = new Outbox.Outbox(
            store,
            new OutboxEnvelopeFactory(registry, serializer, TimeProvider.System, protector));

        await outbox.EnqueueAsync(OutboxEnqueueItem<TestEvent>.From(new TestEvent { Value = "secret" })).ConfigureAwait(false);

        var stored = store.GetAll().Single();
        var dispatcher = new EventOutboxDispatcher(mediator, registry, serializer, protector);
        await dispatcher.DispatchAsync(stored).ConfigureAwait(false);

        CapturingEventMediator.LastValue.Should().Be("secret");
    }

    /// <summary>
    ///     Prefix-based test encryptor.
    /// </summary>
    private sealed class PrefixPayloadEncryptor : IInboxPayloadProtector
    {
        /// <summary>
        ///     The ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixPayloadEncryptor" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixPayloadEncryptor(string prefix)
        {
            _prefix = prefix;
        }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prefix + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ciphertext[_prefix.Length..]);
        }
    }

    /// <summary>
    ///     Test command payload.
    /// </summary>
    private sealed class TestCommand : ICommand
    {
        /// <summary>
        ///     Gets or sets the value.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    ///     Dispatcher that decrypts and deserializes the stored payload for verification.
    /// </summary>
    private sealed class CapturingInboxDispatcher : IInboxDispatcher
    {
        /// <summary>
        ///     Gets the encryptor used to decrypt stored payloads.
        /// </summary>
        private readonly IInboxPayloadProtector _encryptor;

        /// <summary>
        ///     Gets the serializer used to hydrate payloads.
        /// </summary>
        private readonly IMessageSerializer _serializer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CapturingInboxDispatcher" /> class.
        /// </summary>
        /// <param name="serializer">The serializer used to hydrate payloads.</param>
        /// <param name="encryptor">The encryptor used to decrypt stored payloads.</param>
        public CapturingInboxDispatcher(IMessageSerializer serializer, IInboxPayloadProtector encryptor)
        {
            _serializer = serializer;
            _encryptor = encryptor;
        }

        /// <summary>
        ///     Gets the last deserialized command value.
        /// </summary>
        public static string LastValue { get; private set; } = string.Empty;

        /// <inheritdoc />
        public async Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            var payload = await PayloadProtection.UnprotectAsync(envelope.Payload, _encryptor, cancellationToken).ConfigureAwait(false);

            var command = await _serializer.DeserializeAsync(typeof(TestCommand), payload, cancellationToken).ConfigureAwait(false);

            LastValue = ((TestCommand) command).Value;
        }
    }

    /// <summary>
    ///     Prefix-based inbox protector for dispatch tests.
    /// </summary>
    private sealed class PrefixInboxPayloadProtector : IInboxPayloadProtector
    {
        /// <summary>
        ///     Gets the ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixInboxPayloadProtector" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixInboxPayloadProtector(string prefix)
        {
            _prefix = prefix;
        }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prefix + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ciphertext[_prefix.Length..]);
        }
    }

    /// <summary>
    ///     Prefix-based outbox protector for dispatch tests.
    /// </summary>
    private sealed class PrefixOutboxPayloadProtector : IOutboxPayloadProtector
    {
        /// <summary>
        ///     Gets the ciphertext prefix.
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PrefixOutboxPayloadProtector" /> class.
        /// </summary>
        /// <param name="prefix">The ciphertext prefix.</param>
        public PrefixOutboxPayloadProtector(string prefix)
        {
            _prefix = prefix;
        }

        /// <inheritdoc />
        public Task<string> EncryptAsync(string plaintext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prefix + plaintext);
        }

        /// <inheritdoc />
        public Task<string> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ciphertext[_prefix.Length..]);
        }
    }

    /// <summary>
    ///     Captures the last command sent through the in-process mediator.
    /// </summary>
    private sealed class CapturingCommandMediator : ICommandMediator
    {
        /// <summary>
        ///     Gets the last command value observed by the mediator.
        /// </summary>
        public static string LastValue { get; private set; } = string.Empty;

        /// <inheritdoc />
        public Task SendAsync(ICommand command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            LastValue = ((TestCommand) command).Value;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CommandMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    ///     Captures the last event published through the in-process mediator.
    /// </summary>
    private sealed class CapturingEventMediator : IEventMediator
    {
        /// <summary>
        ///     Gets the last event value observed by the mediator.
        /// </summary>
        public static string LastValue { get; private set; } = string.Empty;

        /// <inheritdoc />
        public Task PublishAsync(IEvent @event, EventMediationSettings? settings = null, CancellationToken cancellationToken = default)
        {
            LastValue = ((TestEvent) @event).Value;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PublishAsync<TEvent>(TEvent @event, EventMediationSettings? settings = null, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            LastValue = ((TestEvent) (object) @event).Value;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Test event payload.
    /// </summary>
    private sealed class TestEvent : IEvent
    {
        /// <summary>
        ///     Gets or sets the value.
        /// </summary>
        public string Value { get; set; } = string.Empty;
    }
}