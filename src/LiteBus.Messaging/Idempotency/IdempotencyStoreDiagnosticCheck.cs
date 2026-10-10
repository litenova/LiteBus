using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Messaging.Idempotency;

/// <summary>
///     Reports whether idempotency can actually remember anything, for an application that enabled it.
/// </summary>
/// <remarks>
///     Enabling idempotency registers the shortcuts and the completion handler, and all three need an
///     <see cref="IIdempotencyStore" /> the application supplies. Without one, the first declaring command fails inside
///     the shortcut stage. A probe turns that into an answer an operator can read before the first message arrives.
/// </remarks>
public sealed class IdempotencyStoreDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The name reported by this probe.
    /// </summary>
    public const string CheckName = "litebus.idempotency.store";

    /// <summary>
    ///     Resolves the store without requiring it, so a missing registration is reported rather than thrown.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="IdempotencyStoreDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to look for a registered store.</param>
    public IdempotencyStoreDiagnosticCheck(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => CheckName;

    /// <inheritdoc />
    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var store = Resolve();

        if (store is null)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "Idempotency is enabled but no IIdempotencyStore is registered, so a repeated command cannot be "
                + "recognised. Register an implementation with the application container.",
                new Dictionary<string, object>
                {
                    ["component"] = "idempotency",
                    ["storeRegistered"] = false
                }));
        }

        return Task.FromResult(new DiagnosticResult(
            DiagnosticStatus.Healthy,
            "Idempotency is enabled and a store is registered.",
            new Dictionary<string, object>
            {
                ["component"] = "idempotency",
                ["storeRegistered"] = true,
                ["storeType"] = store.GetType().FullName ?? store.GetType().Name
            }));
    }

    /// <summary>
    ///     Resolves the store from a dispatch scope, falling back to the provider the probe was given.
    /// </summary>
    /// <returns>The registered store, or <see langword="null" /> when none is registered.</returns>
    /// <remarks>
    ///     A store wrapping a database session is scoped, and resolving a scoped service from a root provider is an
    ///     error in a container validating scopes, so the lookup goes through a dispatch scope where one is available.
    /// </remarks>
    private object? Resolve()
    {
        if (_serviceProvider.GetService(typeof(IMessageDispatchScopeFactory)) is not IMessageDispatchScopeFactory factory)
        {
            return _serviceProvider.GetService(typeof(IIdempotencyStore));
        }

        using var scope = factory.CreateScope();
        return scope.ServiceProvider.GetService(typeof(IIdempotencyStore));
    }
}
