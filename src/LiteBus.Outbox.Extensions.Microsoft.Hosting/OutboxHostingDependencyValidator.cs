using System;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Validates that outbox hosting has the store, processor, and dispatcher dependencies it needs.
/// </summary>
internal static class OutboxHostingDependencyValidator
{
    /// <summary>
    ///     Throws when any required outbox role is missing from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <exception cref="InvalidOperationException">Thrown when a required outbox dependency is not registered.</exception>
    internal static void Validate(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        Require<IOutboxMessageWriter>(serviceProvider, nameof(IOutboxMessageWriter));
        Require<IOutboxMessageLeaseStore>(serviceProvider, nameof(IOutboxMessageLeaseStore));
        Require<IOutboxMessageStateStore>(serviceProvider, nameof(IOutboxMessageStateStore));
        Require<IOutboxProcessor>(serviceProvider, nameof(IOutboxProcessor));
        Require<IOutboxDispatcher>(serviceProvider, nameof(IOutboxDispatcher));
        Require<OutboxProcessorOptions>(serviceProvider, nameof(OutboxProcessorOptions));
        Require<OutboxProcessorHostOptions>(serviceProvider, nameof(OutboxProcessorHostOptions));
    }

    /// <summary>
    ///     Resolves a required service or throws an exception that explains how to register the missing role.
    /// </summary>
    /// <typeparam name="T">The required service type.</typeparam>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <param name="roleName">The missing role name used in the exception message.</param>
    private static void Require<T>(IServiceProvider serviceProvider, string roleName)
        where T : notnull
    {
        if (serviceProvider.GetService(typeof(T)) is null)
        {
            throw new InvalidOperationException(
                $"Outbox processor hosting requires '{roleName}'. Register the outbox module, store roles, dispatcher, and AddOutboxProcessorHosting() before starting the host.");
        }
    }
}
