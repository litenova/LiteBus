using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Queries;

/// <summary>
///     Module for configuring query handling infrastructure.
///     Depends on the messaging module for core messaging functionality.
/// </summary>
public sealed class QueryModule : IModule, IRequires<MessageModule>
{
    /// <summary>
    ///     The configuration callbacks invoked when the module is built, in registration order.
    /// </summary>
    private readonly List<Action<QueryModuleBuilder>> _builders = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the query module.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public QueryModule(Action<QueryModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builders.Add(builder);
    }

    /// <summary>
    ///     Runs an additional configuration callback on this module's builder when the module builds.
    /// </summary>
    /// <param name="builderAction">The additional query configuration callback.</param>
    /// <param name="registerChild">Registers child modules with the module registry; the query module declares none.</param>
    internal void Configure(Action<QueryModuleBuilder> builderAction, Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(builderAction);
        ArgumentNullException.ThrowIfNull(registerChild);
        _builders.Add(builderAction);
    }

    /// <summary>
    ///     Builds the query module by configuring query handlers and registering query-specific services.
    /// </summary>
    /// <param name="configuration">The module configuration containing dependency registry and shared context.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var messageRegistry = configuration.GetContext<IMessageRegistry>();
        var contractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());

        var startIndex = messageRegistry.Handlers.Count;
        var moduleBuilder = new QueryModuleBuilder(messageRegistry, contractRegistry);

        foreach (var configure in _builders)
        {
            configure(moduleBuilder);
        }

        RegisterQueryServices(configuration, moduleBuilder.UnitOfWorkType);
        // Recorded for the composition summary. Only this module knows how many messages belong to its axis, because
        // the messaging registry does not reference the axis contracts.
        if (configuration.TryGetContext<LiteBusCompositionSummary>(out var summary) && summary is not null)
        {
            summary.RecordAxis("queries", ModuleRegistration.CountAxisMessages<IQuery>(messageRegistry));
        }

        ModuleRegistration.RegisterNewHandlers(configuration, messageRegistry, startIndex);
    }

    /// <summary>
    ///     Registers query-specific services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="unitOfWorkType">The unit of work implementation type registered through the builder, or <see langword="null" /> when the application commits its own way.</param>
    private static void RegisterQueryServices(IModuleConfiguration configuration, Type? unitOfWorkType)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IQueryMediator),
            typeof(QueryMediator)));

        if (unitOfWorkType is null)
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            unitOfWorkType,
            unitOfWorkType,
            InstanceLifetime.Scoped));

        // The completion handler takes the concrete type, so each axis commits its own even when commands and queries
        // name different implementations. The shared interface resolves to the first module's registration instead of
        // failing composition with a conflicting binding.
        if (configuration.DependencyRegistry.Any(static descriptor => descriptor.DependencyType == typeof(IUnitOfWork)))
        {
            return;
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IUnitOfWork),
            // Resolve the concrete registration, so IUnitOfWork is the same scoped instance the completion handler commits
            // and the boundary pre-handler begins. A separate type mapping would create a second instance with no
            // transaction, which an application resolving IUnitOfWork (for example as its transaction provider) would see.
            serviceProvider => serviceProvider.GetService(unitOfWorkType)
                ?? throw new LiteBusDependencyResolutionException(unitOfWorkType),
            InstanceLifetime.Scoped));
    }
}
