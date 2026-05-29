using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Shared helpers for inbox and outbox processors.
/// </summary>
public static class MessageProcessorDiagnostics
{
    /// <summary>
    ///     Copies non-empty trace fields from a stored envelope into mediation settings items.
    /// </summary>
    public static void ApplyTraceMetadata(
        IDictionary<string, object> items,
        string? correlationId,
        string? causationId,
        string? tenantId)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            items[MessageTraceContextKeys.CorrelationId] = correlationId;
        }

        if (!string.IsNullOrWhiteSpace(causationId))
        {
            items[MessageTraceContextKeys.CausationId] = causationId;
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            items[MessageTraceContextKeys.TenantId] = tenantId;
        }
    }

    /// <summary>
    ///     Formats an exception for persistence without storing full stack traces.
    /// </summary>
    public static string FormatError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return $"{exception.GetType().FullName}: {exception.Message}";
    }

    /// <summary>
    ///     Validates retry settings used by inbox and outbox processors.
    /// </summary>
    public static void ValidateRetryOptions(RetryOptions retry, string optionsParameterName)
    {
        ArgumentNullException.ThrowIfNull(retry);

        if (retry.MaxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                retry.MaxAttempts,
                "Retry max attempts must be greater than zero.");
        }
    }
}
