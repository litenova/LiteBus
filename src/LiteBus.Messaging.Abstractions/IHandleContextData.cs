using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public interface IHandleContextData
{
    T Get<T>();

    void Set<T>(T value);

    bool Contains<T>();

    void Remove<T>();
}

public class HandleContextData : IHandleContextData
{
    private readonly Dictionary<Type, object> _data = new();

    public T Get<T>()
    {
        return (T) _data[typeof(T)];
    }

    public void Set<T>(T value)
    {
        _data[typeof(T)] = value;
    }

    public bool Contains<T>()
    {
        return _data.ContainsKey(typeof(T));
    }

    public void Remove<T>()
    {
        _data.Remove(typeof(T));
    }
}