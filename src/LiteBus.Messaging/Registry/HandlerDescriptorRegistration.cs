using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Applies registry metadata to handler descriptors as they are committed.
/// </summary>
internal static class HandlerDescriptorRegistration
{
    /// <summary>
    ///     Returns a copy of the descriptor with the supplied registration sequence.
    /// </summary>
    /// <param name="descriptor">The handler descriptor to annotate.</param>
    /// <param name="registrationSequence">The registration sequence assigned by the message registry.</param>
    /// <returns>The descriptor annotated with <see cref="IHandlerDescriptor.RegistrationSequence" />.</returns>
    public static IHandlerDescriptor WithRegistrationSequence(IHandlerDescriptor descriptor, int registrationSequence)
    {
        return descriptor switch
        {
            MainHandlerDescriptor main => new MainHandlerDescriptor
            {
                MessageType = main.MessageType,
                MessageResultType = main.MessageResultType,
                Priority = main.Priority,
                RegistrationSequence = registrationSequence,
                Tags = main.Tags,
                HandlerType = main.HandlerType,
                ContractType = main.ContractType
            },
            PreHandlerDescriptor pre => new PreHandlerDescriptor
            {
                MessageType = pre.MessageType,
                Priority = pre.Priority,
                RegistrationSequence = registrationSequence,
                Tags = pre.Tags,
                HandlerType = pre.HandlerType,
                ContractType = pre.ContractType,
                Stage = pre.Stage,
                Dispatch = pre.Dispatch
            },
            RefusalMapperDescriptor mapper => new RefusalMapperDescriptor
            {
                MessageType = mapper.MessageType,
                MessageResultType = mapper.MessageResultType,
                Priority = mapper.Priority,
                RegistrationSequence = registrationSequence,
                Tags = mapper.Tags,
                HandlerType = mapper.HandlerType,
                ContractType = mapper.ContractType,
                Dispatch = mapper.Dispatch
            },
            PostHandlerDescriptor post => new PostHandlerDescriptor
            {
                MessageType = post.MessageType,
                MessageResultType = post.MessageResultType,
                Priority = post.Priority,
                RegistrationSequence = registrationSequence,
                Tags = post.Tags,
                HandlerType = post.HandlerType,
                ContractType = post.ContractType,
                Dispatch = post.Dispatch
            },
            CompletionHandlerDescriptor completion => new CompletionHandlerDescriptor
            {
                MessageType = completion.MessageType,
                MessageResultType = completion.MessageResultType,
                Priority = completion.Priority,
                RegistrationSequence = registrationSequence,
                Tags = completion.Tags,
                HandlerType = completion.HandlerType,
                ContractType = completion.ContractType,
                Dispatch = completion.Dispatch
            },
            ErrorHandlerDescriptor error => new ErrorHandlerDescriptor
            {
                MessageType = error.MessageType,
                MessageResultType = error.MessageResultType,
                Priority = error.Priority,
                RegistrationSequence = registrationSequence,
                Tags = error.Tags,
                HandlerType = error.HandlerType,
                ContractType = error.ContractType
            },
            _ => descriptor
        };
    }
}
