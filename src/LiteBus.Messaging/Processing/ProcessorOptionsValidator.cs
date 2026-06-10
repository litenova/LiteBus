using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;

namespace LiteBus.Messaging.Processing;

/// <summary>
///     Validates shared processor option values used by inbox and outbox workers.
/// </summary>
internal static class ProcessorOptionsValidator
{
    /// <summary>
    ///     Validates processor option values shared by all durable processor implementations.
    /// </summary>
    /// <param name="options">The processor options to validate.</param>
    /// <param name="optionsParameterName">The parameter name used in thrown exceptions.</param>
    public static void Validate(ProcessorOptions options, string optionsParameterName = "options")
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                options.BatchSize,
                "Batch size must be greater than zero.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                options.LeaseDuration,
                "Lease duration must be greater than zero.");
        }

        if (options.DispatcherConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                options.DispatcherConcurrency,
                "Dispatcher concurrency must be greater than zero.");
        }

        if (options.LeaseHeartbeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                options.LeaseHeartbeatInterval,
                "Lease heartbeat interval cannot be negative.");
        }

        if (options.LeaseHeartbeatInterval > TimeSpan.Zero &&
            options.LeaseHeartbeatInterval > options.LeaseDuration / 2)
        {
            throw new ArgumentOutOfRangeException(
                optionsParameterName,
                options.LeaseHeartbeatInterval,
                "Lease heartbeat interval must be less than or equal to half of the lease duration.");
        }

        MessageProcessorDiagnostics.ValidateRetryOptions(options.Retry, optionsParameterName);
    }
}
