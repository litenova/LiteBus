using System;
using System.Collections.Generic;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Runtime.Abstractions;

/// <summary>
///     Configuration interface for modules providing access to dependency registry and shared context.
/// </summary>
public interface IModuleConfiguration
{
    /// <summary>
    ///     Gets the dependency registry for registering services.
    /// </summary>
    IDependencyRegistry DependencyRegistry { get; }

    /// <summary>
    ///     Gets the startup task implementation types registered by modules for host execution.
    /// </summary>
    IReadOnlyList<Type> StartupTasks { get; }

    /// <summary>
    ///     Gets the background service implementation types registered by modules for host execution.
    /// </summary>
    IReadOnlyList<Type> BackgroundServices { get; }

    /// <summary>
    ///     Gets the diagnostic probe descriptors registered by modules for host execution.
    /// </summary>
    IReadOnlyList<DiagnosticCheckDescriptor> DiagnosticChecks { get; }

    /// <summary>
    ///     Registers a startup task implementation type for host execution after dependency registration is applied.
    /// </summary>
    /// <param name="implementationType">The concrete type that implements <see cref="IStartupTask" />.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementationType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="implementationType" /> does not implement <see cref="IStartupTask" />.
    /// </exception>
    void RegisterStartupTask(Type implementationType);

    /// <summary>
    ///     Registers a background service implementation type for host execution after dependency registration is applied.
    /// </summary>
    /// <param name="implementationType">The concrete type that implements <see cref="IBackgroundService" />.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="implementationType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="implementationType" /> does not implement <see cref="IBackgroundService" />.
    /// </exception>
    void RegisterBackgroundService(Type implementationType);

    /// <summary>
    ///     Registers a diagnostic probe implementation type for host execution after dependency registration is applied.
    /// </summary>
    /// <param name="implementationType">The concrete type that implements <see cref="IDiagnosticCheck" />.</param>
    /// <param name="name">The probe name reported to operators and health hosts.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="implementationType" /> or <paramref name="name" /> is <see langword="null" />.
    /// </exception>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="implementationType" /> does not implement <see cref="IDiagnosticCheck" />.
    /// </exception>
    void RegisterDiagnosticCheck(Type implementationType, string name);

    /// <summary>
    ///     Gets the validations registered by modules to run once every module has been built.
    /// </summary>
    IReadOnlyList<Action> CompositionValidations { get; }

    /// <summary>
    ///     Registers a validation that runs after every module has been built.
    /// </summary>
    /// <param name="validate">The validation to run. It reports a problem by throwing.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="validate" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Modules build in dependency order, so a foundational module cannot see what a module built after it
    ///     registered. A rule spanning several modules, such as a requirement that every registered message declares a
    ///     given value, has nothing to check while the module holding the rule is being built. Registering it here runs
    ///     it once the registry is complete and still at composition time, which is where a configuration error belongs:
    ///     a startup task would report it after the host has already started.
    /// </remarks>
    void RegisterCompositionValidation(Action validate);

    /// <summary>
    ///     Gets a context object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of context to retrieve.</typeparam>
    /// <returns>The context object of the specified type.</returns>
    /// <exception cref="System.InvalidOperationException">Thrown when the context type is not found.</exception>
    T GetContext<T>() where T : class;

    /// <summary>
    ///     Sets a context object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of context to store.</typeparam>
    /// <param name="context">The context object to store.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="Exceptions.LiteBusConfigurationException">
    ///     Thrown when a different context instance is already registered for <typeparamref name="T" />.
    /// </exception>
    void SetContext<T>(T context) where T : class;

    /// <summary>
    ///     Tries to get a context object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of context to retrieve.</typeparam>
    /// <param name="context">When this method returns, contains the context object if found; otherwise, null.</param>
    /// <returns><see langword="true" /> if the context was found; otherwise, <see langword="false" />.</returns>
    bool TryGetContext<T>(out T? context) where T : class;

    /// <summary>
    ///     Gets a context object of the specified type, or creates it using the provided factory if not found.
    /// </summary>
    /// <typeparam name="T">The type of context to retrieve or create.</typeparam>
    /// <param name="factory">The factory function to create the context if it doesn't exist.</param>
    /// <returns>The existing or newly created context object.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="factory" /> is <see langword="null" />.</exception>
    /// <exception cref="Exceptions.LiteBusConfigurationException">
    ///     Thrown when <paramref name="factory" /> returns <see langword="null" />.
    /// </exception>
    T GetOrCreateContext<T>(Func<T> factory) where T : class;
}
