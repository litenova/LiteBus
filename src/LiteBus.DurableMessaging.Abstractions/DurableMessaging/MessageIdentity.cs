using System;

namespace LiteBus.Messaging.Abstractions.DurableMessaging;

/// <summary>
///     Describes how a durable message identifier is assigned when a message is accepted or enqueued.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="MessageIdentity.Generated" /> when the store should allocate a new identifier.
///         Use <see cref="MessageIdentity.Supplied" /> when an upstream request already owns a stable operation id.
///     </para>
/// </remarks>
public abstract record MessageIdentity
{
    /// <summary>
    ///     Requests that the store generate a new message identifier.
    /// </summary>
    public sealed record Generated : MessageIdentity
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageIdentity.Generated" /> class.
        /// </summary>
        private Generated()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when no caller-supplied identifier is available.
        /// </summary>
        public static Generated Instance { get; } = new();
    }

    /// <summary>
    ///     Carries a caller-supplied message identifier that must be stored with the envelope.
    /// </summary>
    /// <param name="Value">The message identifier supplied by the caller.</param>
    public sealed record Supplied(Guid Value) : MessageIdentity;
}
