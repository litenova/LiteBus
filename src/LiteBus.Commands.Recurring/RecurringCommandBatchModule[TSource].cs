using System;
using System.Linq;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     Registers the background loop that sends one batch from a source repeatedly.
/// </summary>
/// <typeparam name="TSource">The batch source type the loop resolves on each pass.</typeparam>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderRecurringExtensions.AddRecurringBatch{TSource}(CommandModuleBuilder, System.Action{RecurringCommandHostOptions})" />, so the loop is composed inside
///     <c>AddCommands</c> and cannot be registered without the axis that mediates it. Registering the same source type
///     twice declares the same module type twice, which fails composition through the registry's duplicate-module rule.
/// </remarks>
public sealed class RecurringCommandBatchModule<TSource> : IModule, IRequires<CommandModule>
    where TSource : class, IRecurringCommandBatchSource
{
    /// <summary>
    ///     The lifecycle and timing settings for the loop.
    /// </summary>
    private readonly RecurringCommandHostOptions _hostOptions;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandBatchModule{TSource}" /> class.
    /// </summary>
    /// <param name="hostOptions">The lifecycle and timing settings for the loop.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hostOptions" /> is <see langword="null" />.</exception>
    public RecurringCommandBatchModule(RecurringCommandHostOptions hostOptions)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        _hostOptions = hostOptions;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _hostOptions.Validate();

        if (_hostOptions.RequireLease)
        {
            // The lease registers through a sibling child module that may build after this one, so the check is
            // deferred until every module has built. It still fails at composition time: a loop that silently ran on
            // every instance after asking for single-flight would be worse than a boot failure.
            configuration.RegisterCompositionValidation(() => ThrowIfLeaseMissing(configuration));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TSource),
            typeof(TSource),
            InstanceLifetime.Scoped));

        // Each source type gets its own loop and its own options, so the service is built by a factory that captures
        // this module's options. Registering the options by their own type would collide as soon as two loops exist.
        var hostOptions = _hostOptions;

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(RecurringCommandBatchBackgroundService<TSource>),
            services =>
            {
                var scopeFactory = (IMessageDispatchScopeFactory?) services.GetService(typeof(IMessageDispatchScopeFactory));
                if (scopeFactory is null)
                {
                    throw new LiteBusDependencyResolutionException(typeof(IMessageDispatchScopeFactory));
                }

                var timeProvider = (TimeProvider?) services.GetService(typeof(TimeProvider)) ?? TimeProvider.System;
                var logger = (ILogger<RecurringCommandBatchBackgroundService<TSource>>?) services.GetService(
                    typeof(ILogger<RecurringCommandBatchBackgroundService<TSource>>));

                return new RecurringCommandBatchBackgroundService<TSource>(hostOptions, scopeFactory, timeProvider, logger);
            }));

        configuration.RegisterBackgroundService(typeof(RecurringCommandBatchBackgroundService<TSource>));
    }

    /// <summary>
    ///     Throws when the loop requires single-flight but no lease implementation was registered.
    /// </summary>
    /// <param name="configuration">The module configuration holding the completed dependency registry.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     No <see cref="IRecurringCommandLease" /> registration is present.
    /// </exception>
    private static void ThrowIfLeaseMissing(IModuleConfiguration configuration)
    {
        var leaseRegistered = configuration.DependencyRegistry
            .Any(static descriptor => descriptor.DependencyType == typeof(IRecurringCommandLease));

        if (!leaseRegistered)
        {
            throw new LiteBusConfigurationException(
                $"The recurring batch source '{typeof(TSource).FullName}' requires a lease "
                + "(RecurringCommandHostOptions.RequireLease is true) but no IRecurringCommandLease is registered. "
                + "Register one through CommandModuleBuilder.RegisterChildModule, for example with "
                + "UsePostgreSqlRecurringLease from LiteBus.Commands.Recurring.Storage.PostgreSql.");
        }
    }
}
