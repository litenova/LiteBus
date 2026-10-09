using System;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Registry;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Queries;

/// <summary>
///     Module for configuring query handling infrastructure.
///     Depends on the messaging module for core messaging functionality.
/// </summary>
public sealed class QueryModule : IModule, IRequires<MessageModule>
{
    /// <summary>
    ///     Gets the configuration action invoked when the module is built.
    /// </summary>
    private readonly Action<QueryModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="QueryModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the query module.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public QueryModule(Action<QueryModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
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
        _builder(moduleBuilder);

        // AddAuditing on the messaging module selects the axes, so a consumer configures the feature once instead of
        // repeating the decision here. EnableAuditing stays the primitive, and registering the same handler twice is a
        // no-op in the registry.
        if (configuration.TryGetContext<AuditingComposition>(out var auditing) && auditing?.Queries == true)
        {
            moduleBuilder.EnableAuditing();
        }

        if (moduleBuilder.AuditingEnabled)
        {
            configuration.RegisterDiagnosticCheck(typeof(AuditTrailDiagnosticCheck), AuditTrailDiagnosticCheck.CheckName);
        }

        RegisterQueryServices(configuration);
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
    private static void RegisterQueryServices(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IQueryMediator),
            typeof(QueryMediator)));
    }
}