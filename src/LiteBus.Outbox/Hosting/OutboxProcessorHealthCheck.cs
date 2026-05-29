using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Outbox.Hosting;

/// <summary>
///     Reports whether the outbox processor host loop is running successfully.
/// </summary>
public sealed class OutboxProcessorHealthCheck : IHealthCheck
{
    private readonly IOutboxProcessorHostState _state;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxProcessorHealthCheck" /> class.
    /// </summary>
    /// <param name="state">The host state used to evaluate health.</param>
    public OutboxProcessorHealthCheck(IOutboxProcessorHostState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_state.LastSuccessfulPassAt is null && _state.ConsecutiveFailures == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("The outbox processor host has not completed a pass yet."));
        }

        if (_state.ConsecutiveFailures > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                _state.LastFailureMessage ?? "The outbox processor host reported consecutive failures.",
                data: new Dictionary<string, object>
                {
                    ["consecutiveFailures"] = _state.ConsecutiveFailures,
                    ["lastFailureAt"] = _state.LastFailureAt?.ToString("O") ?? string.Empty
                }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "The outbox processor host completed its latest pass.",
            data: new Dictionary<string, object>
            {
                ["lastSuccessfulPassAt"] = _state.LastSuccessfulPassAt?.ToString("O") ?? string.Empty
            }));
    }
}
