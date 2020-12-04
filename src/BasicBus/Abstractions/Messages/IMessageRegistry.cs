﻿using System;
using System.Collections.Generic;

namespace BasicBus.Abstractions
{
    public interface IMessageRegistry : IReadOnlyDictionary<Type, IMessageDescriptor>
    {
        
    }
}