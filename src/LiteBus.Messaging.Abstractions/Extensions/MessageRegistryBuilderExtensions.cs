using System.Linq;
using System.Reflection;

namespace LiteBus.Messaging.Abstractions.Extensions;

public static class MessageRegistryBuilderExtensions
{
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