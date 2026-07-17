using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Processing;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;
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
    /// <returns>The configured pipelined inbox processor instance.</returns>
    public static IInboxProcessor Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = GetRequiredService<InboxProcessorOptions>(services);
        var clock = services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var leaseStore = GetRequiredService<IInboxLeaseStore>(services);
        var stateWriter = GetRequiredService<IInboxStateWriter>(services);
        var dispatcher = GetRequiredService<IInboxDispatcher>(services);
        var hooks = ResolveHooks(services);

        var dispatchScopeFactory = services.GetService(typeof(IServiceScopeFactory)) is IServiceScopeFactory scopeFactory
            ? new MessageDispatchScopeFactory(scopeFactory)
            : null;

        return new PipelinedInboxProcessor(
            leaseStore,
            stateWriter,
            dispatcher,
            options,
            clock,
            hooks,
            services.GetService(typeof(ILogger<PipelinedInboxProcessor>)) as ILogger<PipelinedInboxProcessor> ?? NullLogger<PipelinedInboxProcessor>.Instance,
            dispatchScopeFactory);
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
        ProcessorOptionsValidator.Validate(options);
    }

    /// <summary>
    ///     Resolves registered inbox processor envelope hooks from the service provider.
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
            $"Inbox processor requires {typeof(T).FullName} to be registered. " +
            "Configure inbox storage and dispatcher inside AddInboxModule(...).");
    }
}
