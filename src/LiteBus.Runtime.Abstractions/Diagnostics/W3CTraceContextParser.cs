using System;
using System.Diagnostics;
using System.Text.Json;

namespace LiteBus.Runtime.Abstractions.Diagnostics;

/// <summary>
///     Parses W3C trace context stored as a trace parent string or a JSON object.
/// </summary>
public static class W3CTraceContextParser
{
    /// <summary>
    ///     The trace parent property name used in serialized distributed trace context.
    /// </summary>
    private const string TraceParentPropertyName = "traceparent";

    /// <summary>
    ///     The trace state property name used in serialized distributed trace context.
    /// </summary>
    private const string TraceStatePropertyName = "tracestate";

    /// <summary>
    ///     Attempts to parse a remote W3C activity context.
    /// </summary>
    /// <param name="serializedContext">
    ///     A W3C trace parent string or a JSON object containing <c>traceparent</c> and optional
    ///     <c>tracestate</c> properties.
    /// </param>
    /// <param name="activityContext">The parsed remote activity context when parsing succeeds.</param>
    /// <returns><see langword="true" /> when a valid trace parent was parsed; otherwise <see langword="false" />.</returns>
    public static bool TryParse(string? serializedContext, out ActivityContext activityContext)
    {
        activityContext = default;

        if (string.IsNullOrWhiteSpace(serializedContext))
        {
            return false;
        }

        var traceParent = serializedContext;
        string? traceState = null;

        if (serializedContext.AsSpan().TrimStart().StartsWith('{'))
        {
            if (!TryReadJson(serializedContext, out traceParent, out traceState))
            {
                return false;
            }
        }

        return ActivityContext.TryParse(traceParent, traceState, true, out activityContext);
    }

    /// <summary>
    ///     Reads trace parent and trace state fields from a JSON context object.
    /// </summary>
    /// <param name="serializedContext">The serialized JSON trace context.</param>
    /// <param name="traceParent">The trace parent field when present and string-typed.</param>
    /// <param name="traceState">The optional trace state field when present and string-typed.</param>
    /// <returns><see langword="true" /> when the JSON contains a string trace parent; otherwise <see langword="false" />.</returns>
    private static bool TryReadJson(
        string serializedContext,
        out string? traceParent,
        out string? traceState)
    {
        traceParent = null;
        traceState = null;

        try
        {
            using var document = JsonDocument.Parse(serializedContext);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(TraceParentPropertyName, out var traceParentProperty) ||
                traceParentProperty.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            traceParent = traceParentProperty.GetString();

            if (root.TryGetProperty(TraceStatePropertyName, out var traceStateProperty))
            {
                if (traceStateProperty.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                traceState = traceStateProperty.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
