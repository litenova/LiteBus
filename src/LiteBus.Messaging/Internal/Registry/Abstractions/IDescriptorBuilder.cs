using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Abstractions;

internal interface IDescriptorBuilder
{
    bool CanBuild(Type type);

    IEnumerable<IDescriptor> Build(Type type);
}