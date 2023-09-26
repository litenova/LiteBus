using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Abstractions;

internal interface IDescriptorBuilder
{
    bool CanBuild(Type type);

    IEnumerable<IDescriptor> Build(Type type);
}

internal interface IDescriptorBuilder<out TDescriptor> : IDescriptorBuilder where TDescriptor : IDescriptor
{
    IEnumerable<IDescriptor> IDescriptorBuilder.Build(Type type)
    {
        return (IEnumerable<IDescriptor>) Build(type);
    }

    new IEnumerable<TDescriptor> Build(Type type);
}