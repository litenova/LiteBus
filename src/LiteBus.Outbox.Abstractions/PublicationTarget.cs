namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Describes how an outbox dispatcher resolves the publication destination for a stored event.
/// </summary>
/// <remarks>
///     <para>
///         Use <see cref="PublicationTarget.ContractDefault" /> when dispatchers should derive the transport target from the stored
///         contract name and module configuration. Use <see cref="PublicationTarget.Topic" /> when callers need an explicit topic or
///         channel stored with the envelope.
///     </para>
/// </remarks>
public abstract record PublicationTarget
{
    /// <summary>
    ///     Indicates that dispatchers should resolve the publication target from the stored contract and defaults.
    /// </summary>
    public sealed record ContractDefault : PublicationTarget
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="PublicationTarget.ContractDefault" /> class.
        /// </summary>
        private ContractDefault()
        {
        }

        /// <summary>
        ///     Gets the singleton instance used when no explicit publication target is supplied.
        /// </summary>
        public static ContractDefault Instance { get; } = new();
    }

    /// <summary>
    ///     Carries an explicit publication topic or channel stored with the outbox envelope.
    /// </summary>
    /// <param name="Name">The topic or channel name dispatchers map to a transport target.</param>
    public sealed record Topic(string Name) : PublicationTarget;

    /// <summary>
    ///     Carries an explicit AMQP exchange name stored with the outbox envelope.
    /// </summary>
    /// <param name="Name">The exchange name dispatchers map to a transport target.</param>
    public sealed record Exchange(string Name) : PublicationTarget;

    /// <summary>
    ///     Carries an explicit AMQP queue name stored with the outbox envelope.
    /// </summary>
    /// <param name="Name">The queue name dispatchers map to a transport target.</param>
    public sealed record Queue(string Name) : PublicationTarget;
}