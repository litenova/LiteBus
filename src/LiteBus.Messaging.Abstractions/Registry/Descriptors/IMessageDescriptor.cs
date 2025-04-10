using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents a descriptor for a message type, providing access to all handlers associated with that message type.
/// </summary>
/// <remarks>
/// Message descriptors serve as a registry of all handlers that can process a specific message type.
/// They categorize handlers into direct handlers (those explicitly registered for the message type)
/// and indirect handlers (those registered for a base type or interface that the message type implements).
/// This allows for flexible message handling based on inheritance and interface implementation.
/// </remarks>
public interface IMessageDescriptor
{
    /// <summary>
    /// Gets the type of the message that this descriptor represents.
    /// </summary>
    Type MessageType { get; }

    /// <summary>
    /// Gets a value indicating whether the message type is a generic type.
    /// </summary>
    /// <remarks>
    /// This property is used to determine if special handling is required for generic message types.
    /// </remarks>
    bool IsGeneric { get; }

    /// <summary>
    /// Gets a read-only collection of main handlers directly registered for this message type.
    /// </summary>
    /// <remarks>
    /// These are handlers that were explicitly registered to handle this specific message type.
    /// </remarks>
    IReadOnlyCollection<IMainHandlerDescriptor> Handlers { get; }

    /// <summary>
    /// Gets a read-only collection of main handlers indirectly applicable to this message type.
    /// </summary>
    /// <remarks>
    /// These are handlers registered for a base type or interface that this message type implements.
    /// </remarks>
    IReadOnlyCollection<IMainHandlerDescriptor> IndirectHandlers { get; }

    /// <summary>
    /// Gets a read-only collection of post-handlers directly registered for this message type.
    /// </summary>
    /// <remarks>
    /// These are post-handlers that were explicitly registered to handle this specific message type.
    /// </remarks>
    IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers { get; }

    /// <summary>
    /// Gets a read-only collection of post-handlers indirectly applicable to this message type.
    /// </summary>
    /// <remarks>
    /// These are post-handlers registered for a base type or interface that this message type implements.
    /// </remarks>
    IReadOnlyCollection<IPostHandlerDescriptor> IndirectPostHandlers { get; }

    /// <summary>
    /// Gets a read-only collection of pre-handlers directly registered for this message type.
    /// </summary>
    /// <remarks>
    /// These are pre-handlers that were explicitly registered to handle this specific message type.
    /// </remarks>
    IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers { get; }

    /// <summary>
    /// Gets a read-only collection of pre-handlers indirectly applicable to this message type.
    /// </summary>
    /// <remarks>
    /// These are pre-handlers registered for a base type or interface that this message type implements.
    /// </remarks>
    IReadOnlyCollection<IPreHandlerDescriptor> IndirectPreHandlers { get; }

    /// <summary>
    /// Gets a read-only collection of error handlers directly registered for this message type.
    /// </summary>
    /// <remarks>
    /// These are error handlers that were explicitly registered to handle this specific message type.
    /// </remarks>
    IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers { get; }

    /// <summary>
    /// Gets a read-only collection of error handlers indirectly applicable to this message type.
    /// </summary>
    /// <remarks>
    /// These are error handlers registered for a base type or interface that this message type implements.
    /// </remarks>
    IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}