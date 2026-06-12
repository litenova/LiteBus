namespace LiteBus.Transport;

/// <summary>
///     Thrown when a second transport module attempts to register <see cref="Abstractions.IMessageTransport" />.
/// </summary>
public sealed class TransportAlreadyRegisteredException : InvalidOperationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TransportAlreadyRegisteredException" /> class.
    /// </summary>
    /// <param name="moduleName">The transport module type that attempted duplicate registration.</param>
    public TransportAlreadyRegisteredException(string moduleName)
        : base($"Transport is already registered. A second {moduleName} cannot replace the active IMessageTransport registration.")
    {
        ModuleName = moduleName;
    }

    /// <summary>
    ///     Gets the transport module type that attempted duplicate registration.
    /// </summary>
    public string ModuleName { get; }
}
