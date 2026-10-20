using System;

namespace LiteBus.Runtime.Abstractions.Exceptions;

/// <summary>
///     Thrown when a handler registration cannot be dispatched: a pipeline marker with no contract naming a message type, an unsupported open generic shape, two refusal mappers at the same level, or an untyped shortcut answering a message that produces a result.
/// </summary>
/// <remarks>
///     Every case is a registration the pipeline would accept and then be unable to run, which is why they are
///     reported at composition rather than on the first message. Catching this category is how a host tells a
///     handler wiring mistake apart from a missing store or a duplicated module.
/// </remarks>
public sealed class PipelineContractException : LiteBusConfigurationException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineContractException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    public PipelineContractException(string message)
        : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelineContractException" /> class.
    /// </summary>
    /// <param name="message">The configuration error message.</param>
    /// <param name="innerException">The exception that caused this configuration failure.</param>
    public PipelineContractException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
