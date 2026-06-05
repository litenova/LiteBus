using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Runtime.Modules;

/// <summary>
///     Default implementation of <see cref="IModuleConfiguration" /> that provides dependency registry access
///     and context management for sharing objects between modules.
/// </summary>
internal sealed class ModuleConfiguration : IModuleConfiguration
{
    /// <summary>
    ///     Shared contexts published by modules during initialization.
    /// </summary>
    private readonly Dictionary<Type, object> _contexts = [];

    /// <summary>
    ///     Startup task implementation types registered for host execution in first-registration order.
    /// </summary>
    private readonly List<Type> _startupTasks = [];

    /// <summary>
    ///     Tracks startup task types already registered so duplicates are ignored without reordering.
    /// </summary>
    private readonly HashSet<Type> _startupTaskTypes = [];

    /// <summary>
    ///     Background service implementation types registered for host execution in first-registration order.
    /// </summary>
    private readonly List<Type> _backgroundServices = [];

    /// <summary>
    ///     Tracks background service types already registered so duplicates are ignored without reordering.
    /// </summary>
    private readonly HashSet<Type> _backgroundServiceTypes = [];

    /// <summary>
    ///     Diagnostic probe descriptors registered for host execution in first-registration order.
    /// </summary>
    private readonly List<DiagnosticCheckDescriptor> _diagnosticChecks = [];

    /// <summary>
    ///     Tracks diagnostic probe types already registered so duplicates are ignored without reordering.
    /// </summary>
    private readonly HashSet<Type> _diagnosticCheckTypes = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleConfiguration" /> class.
    /// </summary>
    /// <param name="dependencyRegistry">The dependency registry for service registration.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="dependencyRegistry" /> is <see langword="null" />.</exception>
    public ModuleConfiguration(IDependencyRegistry dependencyRegistry)
    {
        DependencyRegistry = dependencyRegistry ?? throw new ArgumentNullException(nameof(dependencyRegistry));
    }

    /// <inheritdoc />
    public IDependencyRegistry DependencyRegistry { get; }

    /// <inheritdoc />
    public IReadOnlyList<Type> StartupTasks => [.. _startupTasks];

    /// <inheritdoc />
    public IReadOnlyList<Type> BackgroundServices => [.. _backgroundServices];

    /// <inheritdoc />
    public IReadOnlyList<DiagnosticCheckDescriptor> DiagnosticChecks => [.. _diagnosticChecks];

    /// <inheritdoc />
    public void RegisterStartupTask(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);

        if (!typeof(IStartupTask).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' must implement {nameof(IStartupTask)}.",
                nameof(implementationType));
        }

        if (_startupTaskTypes.Add(implementationType))
        {
            _startupTasks.Add(implementationType);
        }
    }

    /// <inheritdoc />
    public void RegisterBackgroundService(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);

        if (typeof(IStartupTask).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' implements {nameof(IStartupTask)}. " +
                $"Use {nameof(RegisterStartupTask)} instead of {nameof(RegisterBackgroundService)}.",
                nameof(implementationType));
        }

        if (!typeof(IBackgroundService).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' must implement {nameof(IBackgroundService)}.",
                nameof(implementationType));
        }

        if (_backgroundServiceTypes.Add(implementationType))
        {
            _backgroundServices.Add(implementationType);
        }
    }

    /// <inheritdoc />
    public void RegisterDiagnosticCheck(Type implementationType, string name)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!typeof(IDiagnosticCheck).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' must implement {nameof(IDiagnosticCheck)}.",
                nameof(implementationType));
        }

        if (_diagnosticCheckTypes.Add(implementationType))
        {
            _diagnosticChecks.Add(new DiagnosticCheckDescriptor(implementationType, name));
        }
    }

    /// <inheritdoc />
    public T GetContext<T>() where T : class
    {
        if (_contexts.TryGetValue(typeof(T), out var context))
        {
            return (T) context;
        }

        throw new LiteBusConfigurationException(
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

        // Return existing context if found.
        if (_contexts.TryGetValue(contextType, out var existingContext))
        {
            return (T) existingContext;
        }

        // Create new context using factory.
        var newContext = factory();
        _contexts[contextType] = newContext;
        return newContext;
    }
}
