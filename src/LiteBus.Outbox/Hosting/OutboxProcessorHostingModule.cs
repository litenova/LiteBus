using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Hosting;

/// <summary>
///     Registers Microsoft hosting services for the outbox processor loop.
/// </summary>
public sealed class OutboxProcessorHostingModule : IModule
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<OutboxProcessorHostRegistration>(out _))
        {
            throw new InvalidOperationException(
                "Outbox processor hosting requires UseProcessorHost on AddOutboxModule before AddOutboxProcessorHosting.");
        }

        configuration.DependencyRegistry.RegisterHostedService(typeof(OutboxProcessorHostedService));
    }
}
