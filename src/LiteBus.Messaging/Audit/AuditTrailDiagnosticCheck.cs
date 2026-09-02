using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Reports whether auditing is actually able to write, for an application that enabled it.
/// </summary>
/// <remarks>
///     <para>
///         Enabling auditing registers the record writer, and the writer needs an <see cref="IAuditTrail" /> the
///         application supplies. Without one, the first audited mediation fails inside the completion stage, which is the
///         one stage whose faults are deliberately kept away from the caller. A probe turns that into an answer an
///         operator can read before the first message arrives.
///     </para>
///     <para>
///         It also reports whether the resolved trail is the same instance in two different scopes. A trail wrapping a
///         database session is meant to be scoped, and a singleton one holds a single session for the life of the
///         process. That mistake produces no error until the captured session misbehaves under load, so the probe names
///         it while the application is still starting.
///     </para>
///     <para>
///         Every resolution happens inside a dispatch scope rather than against the provider the probe was given. The
///         trail is scoped by default, and resolving a scoped service from a root provider is an error in a container
///         validating scopes, which would make the probe fail on exactly the configuration it is meant to approve.
///     </para>
/// </remarks>
public sealed class AuditTrailDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The name reported by this probe.
    /// </summary>
    public const string CheckName = "litebus.audit.trail";

    /// <summary>
    ///     Resolves the audit trail without requiring it, so a missing registration is reported rather than thrown.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditTrailDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="serviceProvider">The provider used to look for a registered audit trail.</param>
    public AuditTrailDiagnosticCheck(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public string Name => CheckName;

    /// <inheritdoc />
    public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_serviceProvider.GetService(typeof(IMessageDispatchScopeFactory)) is not IMessageDispatchScopeFactory scopeFactory)
        {
            // A host with no dispatch scope factory resolves everything from the root, so the trail can only be a
            // singleton and there is nothing to compare against.
            return Task.FromResult(Describe(_serviceProvider.GetService(typeof(IAuditTrail)), isSingleton: true));
        }

        using var first = scopeFactory.CreateScope();
        var trail = first.ServiceProvider.GetService(typeof(IAuditTrail));

        if (trail is null)
        {
            return Task.FromResult(Describe(trail: null, isSingleton: true));
        }

        using var second = scopeFactory.CreateScope();
        var trailInSecondScope = second.ServiceProvider.GetService(typeof(IAuditTrail));

        return Task.FromResult(Describe(trail, ReferenceEquals(trail, trailInSecondScope)));
    }

    /// <summary>
    ///     Builds the probe result for a resolved trail, or for a missing one.
    /// </summary>
    /// <param name="trail">The resolved trail, or <see langword="null" /> when none is registered.</param>
    /// <param name="isSingleton">Whether two dispatch scopes resolve the same trail instance.</param>
    /// <returns>The probe result.</returns>
    private static DiagnosticResult Describe(object? trail, bool isSingleton)
    {
        if (trail is null)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "Auditing is enabled but no IAuditTrail is registered, so audit records cannot be written. "
                + "Register an implementation with the application container.",
                new Dictionary<string, object>
                {
                    ["component"] = "audit",
                    ["trailRegistered"] = false
                });
        }

        return new DiagnosticResult(
            DiagnosticStatus.Healthy,
            "Auditing is enabled and an audit trail is registered.",
            new Dictionary<string, object>
            {
                ["component"] = "audit",
                ["trailRegistered"] = true,
                ["trailType"] = trail.GetType().FullName ?? trail.GetType().Name,
                ["trailIsSingleton"] = isSingleton
            });
    }
}
