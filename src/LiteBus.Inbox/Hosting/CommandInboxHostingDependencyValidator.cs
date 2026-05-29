using System;
using LiteBus.Inbox.Abstractions;

namespace LiteBus.Inbox.Hosting;

/// <summary>
///     Validates that command inbox hosting has the store and processor dependencies it needs.
/// </summary>
internal static class CommandInboxHostingDependencyValidator
{
    /// <summary>
    ///     Throws when any required inbox role is missing from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The application service provider.</param>
    /// <exception cref="InvalidOperationException">Thrown when a required inbox dependency is not registered.</exception>
    internal static void Validate(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        Require<ICommandInboxWriter>(serviceProvider, nameof(ICommandInboxWriter));
        Require<ICommandInboxLeaseStore>(serviceProvider, nameof(ICommandInboxLeaseStore));
        Require<ICommandInboxStateStore>(serviceProvider, nameof(ICommandInboxStateStore));
        Require<Abstractions.ICommandInboxProcessor>(serviceProvider, nameof(Abstractions.ICommandInboxProcessor));
        Require<CommandInboxProcessorOptions>(serviceProvider, nameof(CommandInboxProcessorOptions));
        Require<CommandInboxProcessorHostOptions>(serviceProvider, nameof(CommandInboxProcessorHostOptions));
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
                $"Command inbox processor hosting requires '{roleName}'. Register the command inbox module, store roles, and AddCommandInboxProcessorHosting() before starting the host.");
        }
    }
}
