using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Extensions;

/// <summary>
///     Reflection helpers used by handler descriptor builders.
/// </summary>
internal static class TypeExtensions
{
    /// <summary>
    ///     Preserves exact closed message types while reducing open constructed types to their generic definition.
    /// </summary>
    /// <param name="messageType">The message type discovered from a handler contract or registration request.</param>
    /// <returns>The exact closed type, or the generic definition for an open message shape.</returns>
    public static Type NormalizeMessageRegistrationType(this Type messageType)
    {
        return messageType.IsGenericType && messageType.ContainsGenericParameters
            ? messageType.GetGenericTypeDefinition()
            : messageType;
    }

    /// <summary>
    ///     Returns interfaces on <paramref name="type" /> that match an open generic definition.
    /// </summary>
    /// <param name="type">The type whose interfaces are inspected.</param>
    /// <param name="genericTypeDefinition">The open generic interface definition to match.</param>
    /// <returns>Matching constructed interfaces.</returns>
    public static IEnumerable<Type> GetInterfacesEqualTo(this Type type, Type genericTypeDefinition)
    {
        return type.GetInterfaces()
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericTypeDefinition);
    }

    /// <summary>
    ///     Reads handler execution priority from <see cref="HandlerPriorityAttribute" /> when present.
    /// </summary>
    /// <param name="type">The handler type.</param>
    /// <returns>The configured priority, or <c>0</c> when no attribute exists.</returns>
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

    /// <summary>
    ///     Collects handler tags from <see cref="HandlerTagsAttribute" /> and <see cref="HandlerTagAttribute" />.
    /// </summary>
    /// <param name="type">The handler type.</param>
    /// <returns>Distinct tags declared on the type.</returns>
    public static IReadOnlyCollection<string> GetTagsFromAttribute(this Type type)
    {
        var pluralHandlerTagsAttribute = Attribute.GetCustomAttribute(type, typeof(HandlerTagsAttribute)) as HandlerTagsAttribute;

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
