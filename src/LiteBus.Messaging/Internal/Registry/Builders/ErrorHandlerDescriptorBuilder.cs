using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Extensions;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Builders;

internal sealed class ErrorHandlerDescriptorBuilder : IDescriptorBuilder<IErrorHandlerDescriptor>
{
    public bool CanBuild(Type type)
    {
        return type.IsAssignableTo(typeof(IMessageErrorHandler));
    }

    public IEnumerable<IErrorHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IMessageErrorHandler<,>));
        var order = handlerType.GetOrderFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            yield return new ErrorHandlerDescriptor(handlerType, messageType, order);
        }
    }
}