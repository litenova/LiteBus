using System;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Runtime.Dependencies;

/// <summary>
///     Creates dispatch scopes that resolve dependencies from an explicitly supplied root provider.
/// </summary>
/// <remarks>
///     Custom hosts use this implementation only when they intentionally accept scopeless dispatch.
///     Container adapters register scope-producing implementations instead.
/// </remarks>
public sealed class RootMessageDispatchScopeFactory : IMessageDispatchScopeFactory
{
    /// <summary>
    ///     The root service provider retained for the lifetime of this factory.
    /// </summary>
    private readonly IServiceProvider _rootServiceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RootMessageDispatchScopeFactory" /> class.
    /// </summary>
    /// <param name="rootServiceProvider">The root provider used for every dispatch.</param>
    public RootMessageDispatchScopeFactory(IServiceProvider rootServiceProvider)
    {
        ArgumentNullException.ThrowIfNull(rootServiceProvider);
        _rootServiceProvider = rootServiceProvider;
    }

    /// <inheritdoc />
    public IMessageDispatchScope CreateScope()
    {
        return new RootMessageDispatchScope(_rootServiceProvider);
    }

    /// <summary>
    ///     Exposes the root provider without taking ownership of it.
    /// </summary>
    private sealed class RootMessageDispatchScope : IMessageDispatchScope
    {
        /// <summary>
        ///     The provider exposed for dispatch resolution.
        /// </summary>
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RootMessageDispatchScope" /> class.
        /// </summary>
        /// <param name="serviceProvider">The provider exposed by this scope.</param>
        public RootMessageDispatchScope(IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(serviceProvider);
            _serviceProvider = serviceProvider;
        }

        /// <inheritdoc />
        public IServiceProvider ServiceProvider => _serviceProvider;

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
