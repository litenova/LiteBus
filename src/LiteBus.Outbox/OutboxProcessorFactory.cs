using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Creates outbox processor instances from module configuration and dependency injection.
/// </summary>
internal static class OutboxProcessorFactory
{
    /// <summary>
    ///     Creates an <see cref="IOutboxProcessor" /> from the dependency injection container.
    /// </summary>
    /// <param name="services">The service provider used to resolve processor dependencies.</param>
    /// <returns>The configured outbox processor instance.</returns>
    public static IOutboxProcessor Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = (OutboxProcessorOptions)services.GetService(typeof(OutboxProcessorOptions))!;
        var clock = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var processingStore = (IOutboxProcessingStore)services.GetService(typeof(IOutboxProcessingStore))!;
        var dispatcher = (IOutboxDispatcher)services.GetService(typeof(IOutboxDispatcher))!;

        return options.Architecture switch
        {
            ProcessorArchitecture.Legacy => new LegacySequentialOutboxProcessor(
                processingStore,
                dispatcher,
                options,
                clock,
                services.GetService(typeof(ILogger<LegacySequentialOutboxProcessor>)) as ILogger<LegacySequentialOutboxProcessor>
                ?? NullLogger<LegacySequentialOutboxProcessor>.Instance),
            ProcessorArchitecture.Pipelined => new PipelinedOutboxProcessor(
                processingStore,
                dispatcher,
                options,
                clock,
                services.GetService(typeof(ILogger<PipelinedOutboxProcessor>)) as ILogger<PipelinedOutboxProcessor>
                ?? NullLogger<PipelinedOutboxProcessor>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Architecture, "Unsupported processor architecture.")
        };
    }

    /// <summary>
    ///     Resolves the lease owner name for a processor instance.
    /// </summary>
    /// <param name="options">The processor options.</param>
    /// <returns>The effective lease owner string.</returns>
    public static string ResolveLeaseOwner(OutboxProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : options.LeaseOwner;
    }

    /// <summary>
    ///     Validates processor option values shared by all outbox processor implementations.
    /// </summary>
    /// <param name="options">The processor options to validate.</param>
    public static void ValidateOptions(OutboxProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.BatchSize, "Batch size must be greater than zero.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.LeaseDuration, "Lease duration must be greater than zero.");
        }

        if (options.DispatcherConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DispatcherConcurrency,
                "Dispatcher concurrency must be greater than zero.");
        }

        if (options.LeaseHeartbeatInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.LeaseHeartbeatInterval,
                "Lease heartbeat interval cannot be negative.");
        }

        MessageProcessorDiagnostics.ValidateRetryOptions(options.Retry, nameof(options));
    }
}
