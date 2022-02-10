using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Metadata;
using LiteBus.Messaging.Internal.Extensions;
using LiteBus.Messaging.Internal.Registry.Abstractions;
using LiteBus.Messaging.Internal.Registry.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Builders;

internal class ErrorHandlerDescriptorBuilder : IDescriptorBuilder<IErrorHandlerDescriptor>
{
    public bool CanBuild(Type type)
    {
        return type.GetInterfaces()
                   .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IErrorHandler<,>));
    }

    public IEnumerable<IErrorHandlerDescriptor> Build(Type handlerType)
    {
        var interfaces = handlerType.GetInterfacesEqualTo(typeof(IErrorHandler<,>));

        var order = handlerType.GetOrderFromAttribute();

        foreach (var @interface in interfaces)
        {
            var messageType = @interface.GetGenericArguments()[0];
            var messageResultType = @interface.GetGenericArguments()[1];

            yield return new ErrorHandlerDescriptor(handlerType, messageType, messageResultType, order);
        }
    }
}