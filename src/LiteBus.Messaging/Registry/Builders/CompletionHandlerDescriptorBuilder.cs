using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Extensions;
using LiteBus.Messaging.Registry.Abstractions;
using LiteBus.Messaging.Registry.Descriptors;

namespace LiteBus.Messaging.Registry.Builders;

/// <summary>
///     Discovers <see cref="ICompletionHandlerDescriptor" /> instances from completion handler types.
/// </summary>
public sealed class CompletionHandlerDescriptorBuilder : IHandlerDescriptorBuilder
{
    /// <inheritdoc />
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageCompletionHandler));
    }

    /// <inheritdoc />
    [RequiresUnreferencedCode("Pipeline dispatch closes handler contracts over registered message types.")]
    public IEnumerable<IHandlerDescriptor> Build(Type type)
    {
        // A completion handler observes a message either without its result or with it typed. Both contracts produce the
        // same descriptor kind, and the contract recorded here is what the pipeline later dispatches through.
        var interfaces = type.GetInterfacesEqualTo(typeof(IMessageCompletionHandler<>))
            .Concat(type.GetInterfacesEqualTo(typeof(IMessageCompletionHandler<,>)));

        var priority = type.GetPriorityFromAttribute();
        var tags = type.GetTagsFromAttribute();

        foreach (var @interface in interfaces)
        {
            var arguments = @interface.GetGenericArguments();

            yield return new CompletionHandlerDescriptor
            {
                MessageType = arguments[0].NormalizeMessageRegistrationType(),
                MessageResultType = arguments.Length > 1 ? arguments[1] : null,
                Priority = priority,
                Tags = tags,
                HandlerType = type,
                ContractType = @interface,
                Dispatch = PipelineDispatch.For(@interface)
            };
        }
    }
}
