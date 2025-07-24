using System;
using LiteBus.Runtime.Dependencies;

namespace LiteBus.Runtime.Modules;

/// <summary>
/// Configuration interface for modules providing access to dependency registry and shared context.
/// </summary>
public interface IModuleConfiguration
{
    /// <summary>
    /// Gets the dependency registry for registering services.
    /// </summary>
    IDependencyRegistry DependencyRegistry { get; }

    /// <summary>
    /// Gets a context object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of context to retrieve.</typeparam>
    /// <returns>The context object of the specified type.</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the context type is not found.</exception>
    T GetContext<T>() where T : class;

    /// <summary>
    /// Sets a context object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of context to store.</typeparam>
    /// <param name="context">The context object to store.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when context is null.</exception>
    void SetContext<T>(T context) where T : class;

    /// <summary>
    /// Tries to get a context object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of context to retrieve.</typeparam>
    /// <param name="context">When this method returns, contains the context object if found; otherwise, null.</param>
    /// <returns>True if the context was found; otherwise, false.</returns>
    bool TryGetContext<T>(out T? context) where T : class;

    /// <summary>
    /// Gets a context object of the specified type, or creates it using the provided factory if not found.
    /// </summary>
    /// <typeparam name="T">The type of context to retrieve or create.</typeparam>
    /// <param name="factory">The factory function to create the context if it doesn't exist.</param>
    /// <returns>The existing or newly created context object.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when factory is null.</exception>
    T GetOrCreateContext<T>(Func<T> factory) where T : class;
}