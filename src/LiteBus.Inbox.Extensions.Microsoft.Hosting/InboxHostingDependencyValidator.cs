using System;
using LiteBus.Inbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Validates that inbox hosting has the store, processor, and dispatcher dependencies it needs.
/// </summary>
internal static class InboxHostingDependencyValidator
{
    /// <summary>
    ///     Throws when any required inbox role is missing from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <exception cref="InvalidOperationException">Thrown when a required inbox dependency is not registered.</exception>
    internal static void Validate(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        Require<IInboxStore>(serviceProvider, nameof(IInboxStore));
        Require<IInboxLeaseStore>(serviceProvider, nameof(IInboxLeaseStore));
        Require<IInboxStateStore>(serviceProvider, nameof(IInboxStateStore));
        Require<Abstractions.IInboxProcessor>(serviceProvider, nameof(Abstractions.IInboxProcessor));
        Require<IInboxDispatcher>(serviceProvider, nameof(IInboxDispatcher));
        Require<InboxProcessorOptions>(serviceProvider, nameof(InboxProcessorOptions));
        Require<InboxProcessorHostOptions>(serviceProvider, nameof(InboxProcessorHostOptions));
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
                $"Inbox processor hosting requires '{roleName}'. Register the inbox module, store roles, dispatcher, and AddInboxProcessorHosting() before starting the host.");
        }
    }
}
