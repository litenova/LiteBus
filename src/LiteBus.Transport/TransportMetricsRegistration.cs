using System;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Transport;

/// <summary>
///     Registers observable transport metrics once per module configuration.
/// </summary>
public static class TransportMetricsRegistration
{
    /// <summary>
    ///     Registers transport observable metrics for the calling transport module.
    /// </summary>
    /// <remarks>
    ///     One composition hosts one transport: the first caller records its module type in a shared
    ///     <see cref="TransportCompositionContext" />, and a second transport module fails with
    ///     <see cref="ModuleCompositionException" /> naming both modules instead of colliding on the internal
    ///     metrics registrations. <see cref="TransportObservableMetrics" /> resolves a single
    ///     <see cref="ITransportCircuitBreakerRegistry" /> tagged with one identity, so a second broker would
    ///     never appear on its own instruments. The registry also grows one circuit per distinct destination
    ///     string without a bound, so per-tenant routing keyed by caller-supplied destinations is an unbounded
    ///     key space; per-broker identity with keyed publishers is future work tracked on the roadmap.
    /// </remarks>
    /// <param name="configuration">The module configuration receiving the metrics registration.</param>
    /// <param name="broker">The optional stable broker name recorded on transport metrics.</param>
    /// <param name="moduleType">The calling transport module type used to refuse a second transport.</param>
    /// <exception cref="ModuleCompositionException">
    ///     Thrown when a different transport module already registered metrics for
    ///     <paramref name="configuration" />.
    /// </exception>
    public static void RegisterIfNeeded(IModuleConfiguration configuration, string? broker, Type moduleType)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(moduleType);

        var context = configuration.GetOrCreateContext(() => new TransportCompositionContext());

        if (context.ClaimedBy is not null && context.ClaimedBy != moduleType)
        {
            throw new ModuleCompositionException(
                $"Transport module '{moduleType.FullName}' cannot be composed because transport module " +
                $"'{context.ClaimedBy.FullName}' already registered a transport for this composition. " +
                "One composition hosts one transport.");
        }

        context.ClaimedBy = moduleType;

        if (!string.IsNullOrWhiteSpace(broker))
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(TransportBrokerIdentity),
                new TransportBrokerIdentity(broker)));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TransportObservableMetrics),
            static serviceProvider => new TransportObservableMetrics(serviceProvider),
            InstanceLifetime.Singleton));

        configuration.RegisterStartupTask(typeof(TransportObservableMetricsInitializer));
    }
}
