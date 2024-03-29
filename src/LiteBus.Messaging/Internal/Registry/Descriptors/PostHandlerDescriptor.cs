﻿using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal sealed class PostHandlerDescriptor : HandlerDescriptorBase, IPostHandlerDescriptor
{
    public required Type MessageResultType { get; init; }
}