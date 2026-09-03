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
///         It reports whether an <see cref="IAuditActorResolver" /> is registered, and reports its absence as degraded
///         rather than unhealthy. A trail with no actor still records what happened, so it is worth writing; it just
///         cannot say who is answerable, which is the first question a review asks and not something to discover during
///         one.
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
        // An application that replaced the record writer does not have to use an IAuditTrail at all, so asserting one
        // is registered would report a correct configuration as unhealthy. Read from the composition summary rather
        // than by resolving IAuditRecordWriter, because resolving the built-in one with no trail registered throws,
        // which is the very state this check exists to report.
        if (_serviceProvider.GetService(typeof(LiteBusCompositionSummary)) is LiteBusCompositionSummary summary &&
            summary.AuditRecordWriter is { } writer)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Healthy,
                $"Auditing is enabled and the record writer is {writer}, which replaces the built-in one. What that "
                + "writer records with, and whether it uses an IAuditTrail or an IAuditActorResolver at all, is the "
                + "application's to check.",
                new Dictionary<string, object>
                {
                    ["component"] = "audit",
                    ["recordWriter"] = writer
                }));
        }

        if (_serviceProvider.GetService(typeof(IMessageDispatchScopeFactory)) is not IMessageDispatchScopeFactory scopeFactory)
        {
            // A host with no dispatch scope factory resolves everything from the root, so the trail can only be a
            // singleton and there is nothing to compare against.
            return Task.FromResult(Describe(
                _serviceProvider.GetService(typeof(IAuditTrail)),
                isSingleton: true,
                _serviceProvider.GetService(typeof(IAuditActorResolver)) is not null));
        }

        using var first = scopeFactory.CreateScope();
        var trail = first.ServiceProvider.GetService(typeof(IAuditTrail));
        var hasActorResolver = first.ServiceProvider.GetService(typeof(IAuditActorResolver)) is not null;

        if (trail is null)
        {
            return Task.FromResult(Describe(trail: null, isSingleton: true, hasActorResolver));
        }

        using var second = scopeFactory.CreateScope();
        var trailInSecondScope = second.ServiceProvider.GetService(typeof(IAuditTrail));

        return Task.FromResult(Describe(trail, ReferenceEquals(trail, trailInSecondScope), hasActorResolver));
    }

    /// <summary>
    ///     Builds the probe result for a resolved trail, or for a missing one.
    /// </summary>
    /// <param name="trail">The resolved trail, or <see langword="null" /> when none is registered.</param>
    /// <param name="isSingleton">Whether two dispatch scopes resolve the same trail instance.</param>
    /// <param name="hasActorResolver">Whether an <see cref="IAuditActorResolver" /> is registered.</param>
    /// <returns>The probe result.</returns>
    private static DiagnosticResult Describe(object? trail, bool isSingleton, bool hasActorResolver)
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
                    ["trailRegistered"] = false,
                    ["actorResolverRegistered"] = hasActorResolver
                });
        }

        var data = new Dictionary<string, object>
        {
            ["component"] = "audit",
            ["trailRegistered"] = true,
            ["trailType"] = trail.GetType().FullName ?? trail.GetType().Name,
            ["trailIsSingleton"] = isSingleton,
            ["actorResolverRegistered"] = hasActorResolver
        };

        if (!hasActorResolver)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Degraded,
                "Auditing is enabled and an audit trail is registered, but no IAuditActorResolver is, so every record "
                + "is written with no actor. Register one with UseAuditActorResolver on the messaging module builder.",
                data);
        }

        return new DiagnosticResult(
            DiagnosticStatus.Healthy,
            "Auditing is enabled and an audit trail is registered.",
            data);
    }
}
