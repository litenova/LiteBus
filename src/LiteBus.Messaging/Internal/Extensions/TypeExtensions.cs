using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Extensions;

internal static class TypeExtensions
{
    public static IEnumerable<Type> GetInterfacesEqualTo(this Type type, Type genericTypeDefinition)
    {
        return type.GetInterfaces()
                   .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericTypeDefinition);
    }

    public static int GetOrderFromAttribute(this Type type)
    {
        var handlerOrderAttribute = Attribute.GetCustomAttribute(type, typeof(HandlerOrderAttribute));

        int order = default;

        if (handlerOrderAttribute is not null)
        {
            order = ((HandlerOrderAttribute) handlerOrderAttribute).Order;
        }

        return order;
    }
}