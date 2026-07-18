using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Processing;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
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
    /// <returns>The configured pipelined outbox processor instance.</returns>
    public static IOutboxProcessor Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = GetRequiredService<OutboxProcessorOptions>(services);
        var clock = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var leaseStore = GetRequiredService<IOutboxLeaseStore>(services);
        var stateWriter = GetRequiredService<IOutboxStateWriter>(services);
        var dispatcher = GetRequiredService<IOutboxDispatcher>(services);
        var hooks = ResolveHooks(services);

        var dispatchScopeFactory = GetRequiredService<IMessageDispatchScopeFactory>(services);

        return new PipelinedOutboxProcessor(
            leaseStore,
            stateWriter,
            dispatcher,
            options,
            clock,
            hooks,
            services.GetService(typeof(ILogger<PipelinedOutboxProcessor>)) as ILogger<PipelinedOutboxProcessor> ?? NullLogger<PipelinedOutboxProcessor>.Instance,
            dispatchScopeFactory);
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
        ProcessorOptionsValidator.Validate(options);
    }

    /// <summary>
    ///     Resolves registered orchestration processor envelope hooks from the service provider.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <returns>The registered hooks in registration order.</returns>
    private static IProcessorEnvelopeHook[] ResolveHooks(IServiceProvider services)
    {
        if (services.GetService(typeof(IEnumerable<IProcessorEnvelopeHook>)) is IEnumerable<IProcessorEnvelopeHook> hooks)
        {
            return hooks.ToArray();
        }

        if (services.GetService(typeof(IProcessorEnvelopeHook)) is IProcessorEnvelopeHook hook)
        {
            return [hook];
        }

        return [];
    }

    /// <summary>
    ///     Resolves a required processor dependency or throws a configuration exception.
    /// </summary>
    /// <typeparam name="T">The dependency type to resolve.</typeparam>
    /// <param name="services">The service provider.</param>
    /// <returns>The resolved dependency instance.</returns>
    private static T GetRequiredService<T>(IServiceProvider services)
        where T : class
    {
        if (services.GetService(typeof(T)) is T service)
        {
            return service;
        }

        throw new LiteBusConfigurationException(
            $"Outbox processor requires {typeof(T).FullName} to be registered. " +
            "Configure outbox storage and dispatcher inside AddOutboxModule(...).");
    }
}
