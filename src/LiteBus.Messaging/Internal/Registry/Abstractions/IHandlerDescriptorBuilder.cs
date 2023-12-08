using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Abstractions;

internal interface IHandlerDescriptorBuilder
{
    bool CanBuild(Type type);

    IEnumerable<IHandlerDescriptor> Build(Type type);
}