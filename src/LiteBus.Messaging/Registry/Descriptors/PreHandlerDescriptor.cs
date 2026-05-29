using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes a pre-handler registered for a message type.
/// </summary>
internal sealed class PreHandlerDescriptor : HandlerDescriptorBase, IPreHandlerDescriptor;
