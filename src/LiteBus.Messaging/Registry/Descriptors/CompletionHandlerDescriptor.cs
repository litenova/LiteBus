using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes a completion handler registered for a message type.
/// </summary>
internal sealed class CompletionHandlerDescriptor : HandlerDescriptorBase, ICompletionHandlerDescriptor;
