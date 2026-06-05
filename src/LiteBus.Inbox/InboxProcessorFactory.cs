using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Creates inbox processor instances from module configuration and dependency injection.
/// </summary>
internal static class InboxProcessorFactory
{
    /// <summary>
    ///     Creates an <see cref="Abstractions.IInboxProcessor" /> from the dependency injection container.
    /// </summary>
    /// <param name="services">The service provider used to resolve processor dependencies.</param>
    /// <returns>The configured inbox processor instance.</returns>
    public static Abstractions.IInboxProcessor Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = (InboxProcessorOptions)services.GetService(typeof(InboxProcessorOptions))!;
        var clock = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var processingStore = (IInboxProcessingStore)services.GetService(typeof(IInboxProcessingStore))!;
        var dispatcher = (IInboxDispatcher)services.GetService(typeof(IInboxDispatcher))!;
        var hooks = ResolveHooks(services);

        return options.Architecture switch
        {
            ProcessorArchitecture.Legacy => new LegacySequentialInboxProcessor(
                processingStore,
                dispatcher,
                options,
                clock,
                hooks,
                services.GetService(typeof(ILogger<LegacySequentialInboxProcessor>)) as ILogger<LegacySequentialInboxProcessor>
                ?? NullLogger<LegacySequentialInboxProcessor>.Instance),
            ProcessorArchitecture.Pipelined => new PipelinedInboxProcessor(
                processingStore,
                dispatcher,
                options,
                clock,
                hooks,
                services.GetService(typeof(ILogger<PipelinedInboxProcessor>)) as ILogger<PipelinedInboxProcessor>
                ?? NullLogger<PipelinedInboxProcessor>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.Architecture, "Unsupported processor architecture.")
        };
    }

    /// <summary>
    ///     Resolves the lease owner name for a processor instance.
    /// </summary>
    /// <param name="options">The processor options.</param>
    /// <returns>The effective lease owner string.</returns>
    public static string ResolveLeaseOwner(InboxProcessorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : options.LeaseOwner;
    }

    /// <summary>
    ///     Validates processor option values shared by all inbox processor implementations.
    /// </summary>
    /// <param name="options">The processor options to validate.</param>
    public static void ValidateOptions(InboxProcessorOptions options)
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

    /// <summary>
    ///     Resolves registered inbox processor envelope hooks from the service provider.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <returns>The registered hooks in registration order.</returns>
    private static IReadOnlyList<IInboxProcessorEnvelopeHook> ResolveHooks(IServiceProvider services)
    {
        if (services.GetService(typeof(IEnumerable<IInboxProcessorEnvelopeHook>)) is IEnumerable<IInboxProcessorEnvelopeHook> hooks)
        {
            return hooks.ToArray();
        }

        if (services.GetService(typeof(IInboxProcessorEnvelopeHook)) is IInboxProcessorEnvelopeHook hook)
        {
            return [hook];
        }

        return Array.Empty<IInboxProcessorEnvelopeHook>();
    }
}
