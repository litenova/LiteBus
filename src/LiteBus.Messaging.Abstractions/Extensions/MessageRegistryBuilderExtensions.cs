using System.Linq;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions.Extensions;

public static class MessageRegistryBuilderExtensions
{
    public static IMessageRegistry RegisterHandler<THandler>(this IMessageRegistry builder)
        where THandler : IHandler
    {
        builder.Register(typeof(THandler));

        return builder;
    }

    public static IMessageRegistry RegisterPreHandler<TEventPreHandler>(this IMessageRegistry builder)
        where TEventPreHandler : IPreHandler
    {
        builder.Register(typeof(TEventPreHandler));

        return builder;
    }

    public static IMessageRegistry RegisterPostHandler<TEventPostHandler>(this IMessageRegistry builder)
        where TEventPostHandler : IPostHandler
    {
        builder.Register(typeof(TEventPostHandler));

        return builder;
    }

    public static IMessageRegistry RegisterErrorHandler<TEventErrorHandler>(this IMessageRegistry builder)
        where TEventErrorHandler : IErrorHandler
    {
        builder.Register(typeof(TEventErrorHandler));

        return builder;
    }

    public static IMessageRegistry RegisterFrom<TMarker>(this IMessageRegistry builder, Assembly assembly)
    {
        var types = assembly.DefinedTypes.Where(t => t.IsAssignableTo(typeof(TMarker)));

        foreach (var typeInfo in types)
        {
            builder.Register(typeInfo);
        }

        return builder;
    }

    public static IMessageRegistry RegisterFrom(this IMessageRegistry builder, Assembly assembly)
    {
        foreach (var typeInfo in assembly.DefinedTypes)
        {
            builder.Register(typeInfo);
        }

        return builder;
    }
}