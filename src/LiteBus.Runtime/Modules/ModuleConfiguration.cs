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
    ///     Background service implementation types registered for host execution in first-registration order.
    /// </summary>
    private readonly List<Type> _backgroundServices = [];

    /// <summary>
    ///     Tracks background service types already registered so duplicates are ignored without reordering.
    /// </summary>
    private readonly HashSet<Type> _backgroundServiceTypes = [];

    /// <summary>
    ///     Validations registered by modules to run once every module has been built, in registration order.
    /// </summary>
    private readonly List<Action> _compositionValidations = [];

    /// <summary>
    ///     Shared contexts published by modules during initialization.
    /// </summary>
    private readonly Dictionary<Type, object> _contexts = [];

    /// <summary>
    ///     Diagnostic probe descriptors registered for host execution in first-registration order.
    /// </summary>
    private readonly List<DiagnosticCheckDescriptor> _diagnosticChecks = [];

    /// <summary>
    ///     Tracks diagnostic probe names by implementation type so conflicting registrations fail during composition.
    /// </summary>
    private readonly Dictionary<Type, string> _diagnosticCheckNamesByType = [];

    /// <summary>
    ///     Startup task implementation types registered for host execution in first-registration order.
    /// </summary>
    private readonly List<Type> _startupTasks = [];

    /// <summary>
    ///     Tracks startup task types already registered so duplicates are ignored without reordering.
    /// </summary>
    private readonly HashSet<Type> _startupTaskTypes = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleConfiguration" /> class.
    /// </summary>
    /// <param name="dependencyRegistry">The dependency registry for service registration.</param>
    /// <exception cref="System.ArgumentNullException">
    ///     Thrown when <paramref name="dependencyRegistry" /> is
    ///     <see langword="null" />.
    /// </exception>
    public ModuleConfiguration(IDependencyRegistry dependencyRegistry)
    {
        ArgumentNullException.ThrowIfNull(dependencyRegistry);

        DependencyRegistry = dependencyRegistry;
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
    public IReadOnlyList<Action> CompositionValidations => [.. _compositionValidations];

    /// <inheritdoc />
    public void RegisterCompositionValidation(Action validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _compositionValidations.Add(validate);
    }

    /// <inheritdoc />
    public void RegisterStartupTask(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        ValidateHostImplementationType(implementationType, typeof(IStartupTask));

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

        ValidateHostImplementationType(implementationType, typeof(IBackgroundService));

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

        ValidateHostImplementationType(implementationType, typeof(IDiagnosticCheck));

        if (_diagnosticCheckNamesByType.TryGetValue(implementationType, out var registeredName))
        {
            if (string.Equals(registeredName, name, StringComparison.Ordinal))
            {
                return;
            }

            throw new ModuleCompositionException(
                $"Diagnostic check type '{implementationType.FullName ?? implementationType.Name}' is already registered " +
                $"as '{registeredName}' and cannot also be registered as '{name}'.");
        }

        _diagnosticCheckNamesByType.Add(implementationType, name);
        _diagnosticChecks.Add(new DiagnosticCheckDescriptor(implementationType, name));
    }

    /// <inheritdoc />
    public T GetContext<T>() where T : class
    {
        if (_contexts.TryGetValue(typeof(T), out var context))
        {
            return (T) context;
        }

        throw new ModuleCompositionException(
            $"Context of type '{typeof(T).Name}' was not found. " +
            "Ensure the module that provides this context has been registered and runs before this module.");
    }

    /// <inheritdoc />
    public void SetContext<T>(T context) where T : class
    {
        ArgumentNullException.ThrowIfNull(context);

        var contextType = typeof(T);

        if (_contexts.TryGetValue(contextType, out var existingContext))
        {
            if (ReferenceEquals(existingContext, context))
            {
                return;
            }

            throw new ModuleCompositionException(
                $"Context of type '{contextType.FullName ?? contextType.Name}' is already registered. " +
                "Each shared module context must have a single owner.");
        }

        _contexts.Add(contextType, context);
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

        var newContext = factory();

        if (newContext is null)
        {
            throw new ModuleCompositionException(
                $"The context factory for '{contextType.FullName ?? contextType.Name}' returned null.");
        }

        _contexts.Add(contextType, newContext);
        return newContext;
    }

    /// <summary>
    ///     Validates a host manifest implementation type before registration.
    /// </summary>
    /// <param name="implementationType">The candidate implementation type.</param>
    /// <param name="contractType">The host manifest contract the implementation must satisfy.</param>
    private static void ValidateHostImplementationType(Type implementationType, Type contractType)
    {
        if (!contractType.IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' must implement {contractType.Name}.",
                nameof(implementationType));
        }

        if (!implementationType.IsClass || implementationType.IsAbstract)
        {
            throw new ArgumentException(
                $"Type '{implementationType.FullName ?? implementationType.Name}' must be a concrete class.",
                nameof(implementationType));
        }
    }
}
