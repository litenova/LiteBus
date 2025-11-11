using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Modules;

/// <summary>
///     Default implementation of <see cref="IModuleConfiguration" /> that provides dependency registry access
///     and context management for sharing objects between modules.
/// </summary>
internal sealed class ModuleConfiguration : IModuleConfiguration
{
    private readonly Dictionary<Type, object> _contexts = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleConfiguration" /> class.
    /// </summary>
    /// <param name="dependencyRegistry">The dependency registry for service registration.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when dependencyRegistry is null.</exception>
    public ModuleConfiguration(IDependencyRegistry dependencyRegistry)
    {
        DependencyRegistry = dependencyRegistry ?? throw new ArgumentNullException(nameof(dependencyRegistry));
    }

    /// <inheritdoc />
    public IDependencyRegistry DependencyRegistry { get; }

    /// <inheritdoc />
    public T GetContext<T>() where T : class
    {
        if (_contexts.TryGetValue(typeof(T), out var context))
        {
            return (T) context;
        }

        throw new InvalidOperationException(
            $"Context of type '{typeof(T).Name}' was not found. " +
            "Ensure the module that provides this context has been registered and runs before this module.");
    }

    /// <inheritdoc />
    public void SetContext<T>(T context) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        var contextType = typeof(T);

        // Allow overwriting existing context (last one wins)
        _contexts[contextType] = context;
    }

    /// <inheritdoc />
    public bool TryGetContext<T>(out T? context) where T : class
    {
        if (_contexts.TryGetValue(typeof(T), out var contextObj))
        {
            context = (T) contextObj;
            return true;
        }

        context = null;
        return false;
    }

    /// <inheritdoc />
    public T GetOrCreateContext<T>(Func<T> factory) where T : class
    {
        ArgumentNullException.ThrowIfNull(factory);

        var contextType = typeof(T);

        // Return existing context if found
        if (_contexts.TryGetValue(contextType, out var existingContext))
        {
            return (T) existingContext;
        }

        // Create new context using factory
        var newContext = factory();
        _contexts[contextType] = newContext;
        return newContext;
    }
}