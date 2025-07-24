using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Extensions;

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

        var order = 0;

        if (handlerOrderAttribute is not null)
        {
            order = ((HandlerOrderAttribute) handlerOrderAttribute).Order;
        }

        return order;
    }

    public static IReadOnlyCollection<string> GetTagsFromAttribute(this Type type)
    {
        // The one and only [HandlerTags] attribute
        var pluralHandlerTagsAttribute = Attribute.GetCustomAttribute(type, typeof(HandlerTagsAttribute)) as HandlerTagsAttribute;

        // The multiple [HandlerTag] attributes
        var singleTagAttributes = Attribute.GetCustomAttributes(type, typeof(HandlerTagAttribute)) as HandlerTagAttribute[];

        var tags = new List<string>();

        if (pluralHandlerTagsAttribute is not null)
        {
            tags.AddRange(pluralHandlerTagsAttribute.Tags);
        }

        if (singleTagAttributes is not null)
        {
            tags.AddRange(singleTagAttributes.Select(x => x.Tag));
        }

        return tags;
    }
}