using System;
using System.Threading;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Provides access to the global singleton instance of <see cref="IMessageRegistry" />.
///     This class ensures only one registry exists across multiple registrations.
/// </summary>
/// <remarks>
///     The <see cref="MessageRegistryAccessor" /> maintains a single instance of <see cref="IMessageRegistry" />
///     throughout the application lifecycle, allowing incremental registration of handlers
///     when <see cref="AddLiteBus" /> is called multiple times.
/// </remarks>
public static class MessageRegistryAccessor
{
    /// <summary>
    ///     Lazy initializer for the message registry that ensures thread-safe initialization.
    /// </summary>
    private static readonly Lazy<IMessageRegistry> LazyInstance = new(() => new MessageRegistry(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     Gets the singleton instance of the message registry.
    ///     The instance is created on first access in a thread-safe manner.
    /// </summary>
    /// <value>
    ///     The global singleton instance of <see cref="IMessageRegistry" /> used for handler registration.
    /// </value>
    public static IMessageRegistry Instance => LazyInstance.Value;
}