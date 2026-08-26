using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Reports whether auditing is actually able to write, for an application that enabled it.
/// </summary>
/// <remarks>
///     Enabling auditing registers the record writer, and the writer needs an <see cref="IAuditTrail" /> the application
///     supplies. Without one, the first audited mediation fails inside the completion stage, which is the one stage whose
///     faults are deliberately kept away from the caller. A probe turns that into an answer an operator can read before
///     the first message arrives.
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
        var trail = _serviceProvider.GetService(typeof(IAuditTrail));

        if (trail is null)
        {
            return Task.FromResult(new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                "Auditing is enabled but no IAuditTrail is registered, so audit records cannot be written. "
                + "Register an implementation with the application container.",
                new Dictionary<string, object>
                {
                    ["component"] = "audit",
                    ["trailRegistered"] = false
                }));
        }

        return Task.FromResult(new DiagnosticResult(
            DiagnosticStatus.Healthy,
            "Auditing is enabled and an audit trail is registered.",
            new Dictionary<string, object>
            {
                ["component"] = "audit",
                ["trailRegistered"] = true,
                ["trailType"] = trail.GetType().FullName ?? trail.GetType().Name
            }));
    }
}
