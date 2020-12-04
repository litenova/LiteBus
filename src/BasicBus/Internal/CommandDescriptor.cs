using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BasicBus.Abstractions;

namespace BasicBus.Internal
{
    internal class CommandDescriptor : ICommandDescriptor
    {
        private readonly List<Type> _interceptors = new List<Type>();

        public CommandDescriptor(Type command, Type handler)
        {
            Command = command;
            Handler = handler;
        }

        public Type Command { get; }
        public Type Handler { get; }
        
        public IReadOnlyCollection<Type> Interceptors => _interceptors.AsReadOnly();
        internal void AddInterceptor(Type type) => _interceptors.Add(type);
    }
}