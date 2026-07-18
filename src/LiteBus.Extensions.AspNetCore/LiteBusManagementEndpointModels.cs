using LiteBus.Runtime.Abstractions.Diagnostics;
using ProcessorState = LiteBus.Inbox.Abstractions.ProcessorState;

namespace LiteBus.Extensions.AspNetCore;

/// <summary>
///     JSON payload types for LiteBus management endpoints.
/// </summary>
public static partial class LiteBusManagementEndpointExtensions
{
    /// <summary>
    ///     JSON payload that confirms an unrestricted message purge.
    /// </summary>
    private sealed record PurgeConfirmRequest
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the caller confirms deleting all rows matched by the filter.
        /// </summary>
        /// <value>
        ///     Must be <see langword="true" /> when the query string does not narrow the purge filter.
        /// </value>
        public bool Confirm { get; set; }
    }

    /// <summary>
    ///     JSON payload for a selective requeue request.
    /// </summary>
    private sealed record RequeueMessagesRequest
    {
        /// <summary>
        ///     Gets or initializes the message identifiers to requeue.
        /// </summary>
        public IReadOnlyList<Guid> MessageIds { get; init; } = [];
    }

    /// <summary>
    ///     JSON payload for inbox processor state responses.
    /// </summary>
    private sealed record ProcessorStateResponse
    {
        /// <summary>
        ///     Gets the reported inbox processor state.
        /// </summary>
        public ProcessorState State { get; init; }
    }

    /// <summary>
    ///     JSON payload for outbox processor state responses.
    /// </summary>
    private sealed record OutboxProcessorStateResponse
    {
        /// <summary>
        ///     Gets the reported outbox processor state.
        /// </summary>
        public Outbox.Abstractions.ProcessorState State { get; init; }
    }

    /// <summary>
    ///     JSON payload for a single diagnostic probe outcome.
    /// </summary>
    private sealed record DiagnosticProbeResponse
    {
        /// <summary>
        ///     Gets the probe name.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        ///     Gets the reported status.
        /// </summary>
        public DiagnosticStatus Status { get; init; }

        /// <summary>
        ///     Gets the probe summary text.
        /// </summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>
        ///     Gets optional structured values from the probe.
        /// </summary>
        public IReadOnlyDictionary<string, object>? Data { get; init; }
    }
}
