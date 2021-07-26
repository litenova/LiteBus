using System;
using System.Collections;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal
{
    public class LazyReadOnlyCollection<T> : ILazyReadOnlyCollection<T>
    {
        private readonly List<Lazy<T>> _list;

        public LazyReadOnlyCollection(IEnumerable<Lazy<T>> source)
        {
            _list = new List<Lazy<T>>(source);
        }

        public IEnumerator<Lazy<T>> GetEnumerator() => _list.GetEnumerator();
        
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => _list.Count;
    }
}