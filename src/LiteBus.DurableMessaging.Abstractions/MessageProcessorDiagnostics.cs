using System;
using System.Collections.Generic;
using System.Diagnostics;
using LiteBus.Runtime.Abstractions.Diagnostics;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Shared helpers for inbox and outbox processors.
/// </summary>
public static class MessageProcessorDiagnostics
{
    /// <summary>
    ///     The maximum number of characters written to a durable envelope error field.
    /// </summary>
    private const int MaxPersistedErrorLength = 1024;

    /// <summary>
    ///     Gets the compact error persisted when dispatch is canceled because lease renewal failed.
    /// </summary>
    public const string LeaseLostDuringProcessingError =
        "Lease lost during processing; scheduling retry.";

    /// <summary>
    ///     Copies non-empty trace fields from a stored envelope into mediation settings items.
    /// </summary>
    /// <param name="items">The mediation items dictionary to populate with trace metadata.</param>
    /// <param name="correlationId">The correlation identifier copied when non-empty.</param>
    /// <param name="causationId">The causation identifier copied when non-empty.</param>
    /// <param name="tenantId">The tenant identifier copied when non-empty.</param>
    /// <param name="traceContext">The trace context JSON copied when non-empty.</param>
    public static void ApplyTraceMetadata(
        IDictionary<string, object> items,
        string? correlationId,
        string? causationId,
        string? tenantId,
        string? traceContext = null)
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

        if (!string.IsNullOrWhiteSpace(traceContext))
        {
            items[MessageTraceContextKeys.TraceContext] = traceContext;
        }
    }

    /// <summary>
    ///     Attempts to parse stored W3C trace context into an <see cref="ActivityContext" /> parent.
    /// </summary>
    /// <param name="traceContext">
    ///     A trace parent string or JSON trace context persisted on an inbox or outbox envelope.
    /// </param>
    /// <param name="parentContext">When parsing succeeds, the W3C parent <see cref="ActivityContext" /> for processor spans.</param>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="traceContext" /> is a valid W3C trace parent; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    public static bool TryGetParentActivityContext(string? traceContext, out ActivityContext parentContext)
    {
        parentContext = default;

        if (string.IsNullOrWhiteSpace(traceContext))
        {
            return false;
        }

        return W3CTraceContextParser.TryParse(traceContext, out parentContext);
    }

    /// <summary>
    ///     Formats an exception for persistence without storing full stack traces.
    /// </summary>
    /// <param name="exception">The exception to format.</param>
    /// <returns>A compact type-and-message string suitable for persistence.</returns>
    public static string FormatError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? "No exception message was provided."
            : exception.Message.Replace('\r', ' ').Replace('\n', ' ');
        var formatted = $"{typeName}: {message}";

        return formatted.Length <= MaxPersistedErrorLength
            ? formatted
            : formatted[..MaxPersistedErrorLength];
    }

    /// <summary>
    ///     Validates retry settings used by inbox and outbox processors.
    /// </summary>
    /// <param name="retry">The retry options to validate.</param>
    /// <param name="optionsParameterName">The parameter name used when throwing validation exceptions.</param>
    public static void ValidateRetryOptions(RetryOptions retry, string optionsParameterName)
    {
        ArgumentNullException.ThrowIfNull(retry);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retry.MaxAttempts, 0, optionsParameterName);

        if (retry.InitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                retry.InitialDelay,
                "Retry initial delay must not be negative.");
        }

        if (retry.MaxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                retry.MaxDelay,
                "Retry maximum delay must not be negative.");
        }

        if (!Enum.IsDefined(retry.Backoff))
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                retry.Backoff,
                "Retry backoff must be a defined value.");
        }
    }
}
