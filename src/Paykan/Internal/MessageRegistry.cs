using System;
using System.Collections.Generic;
using Paykan.Abstractions;

namespace Paykan.Internal
{
    internal class MessageRegistry : Dictionary<Type, IMessageDescriptor>, IMessageRegistry
    {

    }
}