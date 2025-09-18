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

    public static int GetPriorityFromAttribute(this Type type)
    {
        var handlerPriorityAttribute = Attribute.GetCustomAttribute(type, typeof(HandlerPriorityAttribute));

        var priority = 0;

        if (handlerPriorityAttribute is not null)
        {
            priority = ((HandlerPriorityAttribute) handlerPriorityAttribute).Priority;
        }

        return priority;
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