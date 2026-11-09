using System;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Shared composition-time registration steps used by the messaging module and the command, query, and event
///     axis modules.
/// </summary>
/// <remarks>
///     Each module captures <see cref="IMessageReader.Handlers" /> count before running its builder and registers
///     only the handlers appended after that index, so one helper keeps the snapshot rule in one place.
/// </remarks>
internal static class ModuleRegistration
{
    /// <summary>
    ///     Registers handler types that were added to the message registry during module building.
    /// </summary>
    /// <param name="configuration">The module configuration to register handlers with.</param>
    /// <param name="reader">The registry holding the handler descriptors in registration order.</param>
    /// <param name="startIndex">The index from which to start registering new handlers.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="configuration" /> or <paramref name="reader" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     An open generic handler over a bare type parameter, such as <c>Guard&lt;T&gt;</c>, is also registered by its
    ///     open definition. The registry closes it again for a message type first registered at dispatch, after the
    ///     container is built, and the container resolves that closure from the open registration. The dependency
    ///     registry ignores a registration it already holds, so every module registering the same definition is safe.
    /// </remarks>
    public static void RegisterNewHandlers(IModuleConfiguration configuration, IMessageReader reader, int startIndex)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(reader);

        var newHandlers = reader.Handlers.Skip(startIndex);

        foreach (var handlerDescriptor in newHandlers)
        {
            RegisterHandlerType(configuration, handlerDescriptor.HandlerType);
        }

        foreach (var openGenericHandlerType in reader.OpenGenericClosures.Keys)
        {
            RegisterHandlerType(configuration, openGenericHandlerType);
        }
    }

    /// <summary>
    ///     Registers one handler type with scoped lifetime when it can be instantiated.
    /// </summary>
    /// <param name="configuration">The module configuration to register the handler with.</param>
    /// <param name="handlerType">The closed handler type or open generic handler definition.</param>
    private static void RegisterHandlerType(IModuleConfiguration configuration, Type handlerType)
    {
        if (handlerType is { IsClass: true, IsAbstract: false })
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                handlerType,
                handlerType,
                InstanceLifetime.Scoped));
        }
    }

    /// <summary>
    ///     Counts the concrete message types of one axis registered so far, for the composition summary.
    /// </summary>
    /// <typeparam name="TAxis">The axis marker interface, such as the command, query, or event contract.</typeparam>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <returns>The number of concrete message types assignable to <typeparamref name="TAxis" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Abstract types and interfaces are excluded, because they are shapes rather than messages and counting them
    ///     would make the reported total disagree with the number of things that can be mediated.
    /// </remarks>
    public static int CountAxisMessages<TAxis>(IMessageReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var count = 0;

        foreach (var descriptor in reader)
        {
            if (descriptor.MessageType is { IsAbstract: false, IsInterface: false } &&
                typeof(TAxis).IsAssignableFrom(descriptor.MessageType))
            {
                count++;
            }
        }

        return count;
    }
}
