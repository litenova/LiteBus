using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LiteBus.Inbox.Extensions.Microsoft.Hosting;

/// <summary>
///     Reports whether the command inbox processor host loop is running successfully.
/// </summary>
public sealed class CommandInboxProcessorHealthCheck : IHealthCheck
{
    /// <summary>
    ///     Gets the host state used to evaluate health.
    /// </summary>
    private readonly CommandInboxProcessorHostState _state;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandInboxProcessorHealthCheck" /> class.
    /// </summary>
    /// <param name="state">The host state used to evaluate health.</param>
    public CommandInboxProcessorHealthCheck(CommandInboxProcessorHostState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (_state.LastSuccessfulPassAt is null && _state.ConsecutiveFailures == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("The command inbox processor host has not completed a pass yet."));
        }

        if (_state.ConsecutiveFailures > 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                _state.LastFailureMessage ?? "The command inbox processor host reported consecutive failures.",
                data: new Dictionary<string, object>
                {
                    ["consecutiveFailures"] = _state.ConsecutiveFailures,
                    ["lastFailureAt"] = _state.LastFailureAt?.ToString("O") ?? string.Empty
                }));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "The command inbox processor host completed its latest pass.",
            data: new Dictionary<string, object>
            {
                ["lastSuccessfulPassAt"] = _state.LastSuccessfulPassAt?.ToString("O") ?? string.Empty
            }));
    }
}
