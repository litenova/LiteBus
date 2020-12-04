using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BasicBus.Abstractions;

namespace BasicBus.Internal
{
    internal class MessageRegistry : Dictionary<Type, IMessageDescriptor>, IMessageRegistry
    {

    }
}