using System;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Configures immediate dispatch: dispatching the outbox envelopes a mediation staged right after its unit of work
///     commits, with the outbox processor as the fallback.
/// </summary>
/// <remarks>
///     Set through <see cref="OutboxModuleBuilder.EnableImmediateDispatch" />. The values are read once at
///     composition time and validated by <see cref="Validate" />.
/// </remarks>
public sealed class OutboxImmediateDispatchOptions
{
    /// <summary>
    ///     Gets or sets the total time immediate dispatch may take after one commit.
    /// </summary>
    /// <value>
    ///     Ten seconds by default. Envelopes not dispatched in time are released for the outbox processor at once.
    /// </value>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     Gets or sets how long an immediate envelope stays invisible to the outbox processor after it is staged.
    /// </summary>
    /// <value>
    ///     Thirty seconds by default; it must be greater than <see cref="Timeout" />. If the process stops before
    ///     dispatching, the processor delivers the envelope once this delay has passed.
    /// </value>
    public TimeSpan FallbackDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     Gets or sets whether the caller waits for immediate dispatch.
    /// </summary>
    /// <value><see cref="OutboxImmediateDispatchMode.Await" /> by default.</value>
    public OutboxImmediateDispatchMode Mode { get; set; } = OutboxImmediateDispatchMode.Await;

    /// <summary>
    ///     Throws when the values cannot work together.
    /// </summary>
    /// <exception cref="LiteBusConfigurationException">
    ///     <see cref="Timeout" /> is not positive, <see cref="FallbackDelay" /> is not greater than
    ///     <see cref="Timeout" />, or <see cref="Mode" /> is not a defined value.
    /// </exception>
    public void Validate()
    {
        if (Timeout <= TimeSpan.Zero)
        {
            throw new LiteBusConfigurationException(
                $"OutboxImmediateDispatchOptions.Timeout must be positive, but it is {Timeout}.");
        }

        if (FallbackDelay <= Timeout)
        {
            throw new LiteBusConfigurationException(
                $"OutboxImmediateDispatchOptions.FallbackDelay ({FallbackDelay}) must be greater than Timeout ({Timeout}), "
                + "or the outbox processor could take an envelope while immediate dispatch still runs.");
        }

        if (!Enum.IsDefined(Mode))
        {
            throw new LiteBusConfigurationException(
                $"OutboxImmediateDispatchOptions.Mode has the undefined value {(int) Mode}.");
        }
    }
}
