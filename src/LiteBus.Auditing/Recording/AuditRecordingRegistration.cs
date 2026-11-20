using System;
using System.Collections.Generic;
using LiteBus.Auditing.Abstractions;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Composition;
using LiteBus.Messaging.Registry;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing.Recording;

/// <summary>
///     Registers what turns the end of a mediation into an audit record: the record writer, the ambient audit scope,
///     the outcome mapper, the actor resolver, a completion handler per selected axis, the command batch participant,
///     the <c>litebus.audit.trail</c> diagnostic check and the audit composition checks.
/// </summary>
/// <remarks>
///     The completion handlers are pipeline handlers the message registry closes over every message of their axis,
///     so the axis modules carry no audit code. The auditing module builds after the messaging module, which creates
///     the message registry, because it declares <c>IRequires&lt;MessageModule&gt;</c>.
/// </remarks>
internal static class AuditRecordingRegistration
{
    /// <summary>
    ///     Registers the record production services for the selected axes.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="recording">The record-production settings the builder recorded.</param>
    public static void Register(IModuleConfiguration configuration, AuditRecordingOptions recording)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(recording);

        var registry = configuration.DependencyRegistry;
        var messageRegistry = configuration.GetContext<IMessageRegistry>();
        var handlerStart = messageRegistry.Handlers.Count;

        registry.Register(new DependencyDescriptor(typeof(AuditRecordingOptions), recording));

        // The audit scope is stateless: it reads and writes the ambient execution context, so a singleton is correct
        // and stays safe under concurrency because two mediations never share an execution context.
        registry.Register(new DependencyDescriptor(typeof(IAuditScope), new AmbientAuditScope()));

        // The built-in mapper registers as the port default, so the composition model reports whether the application
        // replaced it.
        registry.Register(recording.OutcomeMapper is null
            ? DependencyDescriptor.ForDefaultPort(typeof(IAuditOutcomeMapper), new DefaultAuditOutcomeMapper())
            : new DependencyDescriptor(typeof(IAuditOutcomeMapper), recording.OutcomeMapper));

        RegisterActorResolver(registry, recording);
        RegisterRecordWriter(registry, recording);

        if (recording.Commands)
        {
            messageRegistry.Register(typeof(CommandAuditCompletionHandler));

            // The concrete registration is the one instance per batch scope; the batch reads it through the
            // participant collection, so both must resolve the same object.
            registry.Register(new DependencyDescriptor(
                typeof(CommandBatchAuditParticipant),
                provider => new CommandBatchAuditParticipant((IAuditTrail?) provider.GetService(typeof(IAuditTrail))),
                InstanceLifetime.Scoped));

            registry.RegisterCollection(DependencyDescriptor.ForCollection(
                typeof(ICommandBatchParticipant),
                provider => provider.GetService(typeof(CommandBatchAuditParticipant))
                    ?? throw new LiteBusDependencyResolutionException(typeof(CommandBatchAuditParticipant)),
                InstanceLifetime.Scoped));
        }

        if (recording.Queries)
        {
            messageRegistry.Register(typeof(QueryAuditCompletionHandler));
        }

        if (recording.Events)
        {
            messageRegistry.Register(typeof(EventAuditCompletionHandler));
        }

        // The messaging module registered the handlers it knew about with the container when it built; the completion
        // handlers added here are registered the same way.
        ModuleRegistration.RegisterNewHandlers(configuration, messageRegistry, handlerStart);

        configuration.RegisterDiagnosticCheck(typeof(AuditTrailDiagnosticCheck), AuditTrailDiagnosticCheck.CheckName);

        IReadOnlyList<Action<ICompositionModel>> checks = recording.Checks;

        if (checks.Count > 0)
        {
            var contracts = configuration.GetOrCreateContext(() => new MessageContractRegistry());

            configuration.RegisterCompositionValidation(() =>
            {
                var model = CompositionModel.CreateForValidation(messageRegistry, contracts, [.. registry]);

                foreach (var check in checks)
                {
                    check(model);
                }
            });
        }
    }

    /// <summary>
    ///     Registers the actor resolver the builder was given, when it was given one.
    /// </summary>
    /// <param name="registry">The dependency registry.</param>
    /// <param name="recording">The record-production settings.</param>
    /// <remarks>
    ///     A missing resolver is not a composition failure: the writer attributes the record from the execution
    ///     identity's principal instead, and the diagnostic check reports that no resolver is registered.
    /// </remarks>
    private static void RegisterActorResolver(IDependencyRegistry registry, AuditRecordingOptions recording)
    {
        switch (recording.ActorResolver)
        {
            case null:
                return;
            case Type resolverType:
                registry.Register(new DependencyDescriptor(
                    typeof(IAuditActorResolver),
                    resolverType,
                    recording.ActorResolverLifetime));
                return;
            default:
                registry.Register(new DependencyDescriptor(typeof(IAuditActorResolver), recording.ActorResolver));
                return;
        }
    }

    /// <summary>
    ///     Registers the record writer: the application's replacement, or the built-in writer as the port default.
    /// </summary>
    /// <param name="registry">The dependency registry.</param>
    /// <param name="recording">The record-production settings.</param>
    private static void RegisterRecordWriter(IDependencyRegistry registry, AuditRecordingOptions recording)
    {
        switch (recording.RecordWriter)
        {
            case Type writerType:
                registry.Register(new DependencyDescriptor(
                    typeof(IAuditRecordWriter),
                    writerType,
                    recording.RecordWriterLifetime));
                return;
            case not null:
                registry.Register(new DependencyDescriptor(typeof(IAuditRecordWriter), recording.RecordWriter));
                return;
            default:
                registry.Register(DependencyDescriptor.ForDefaultPort(
                    typeof(IAuditRecordWriter),
                    CreateRecordWriter,
                    InstanceLifetime.Scoped));
                return;
        }
    }

    /// <summary>
    ///     Creates the built-in record writer from the scope's services.
    /// </summary>
    /// <param name="serviceProvider">The scope's service provider.</param>
    /// <returns>The record writer.</returns>
    /// <exception cref="AuditConfigurationException">No <see cref="IAuditTrail" /> is registered.</exception>
    private static AuditRecordWriter CreateRecordWriter(IServiceProvider serviceProvider)
    {
        var trail = (IAuditTrail?) serviceProvider.GetService(typeof(IAuditTrail))
                    ?? throw new AuditConfigurationException(
                        "Auditing is enabled but no IAuditTrail is registered, so audit records cannot be written. "
                        + "Configure an audit store such as UseMartenStore, or UseTrail, on the auditing builder. The "
                        + "litebus.audit.trail diagnostic check reports this before the first message arrives.");

        return new AuditRecordWriter(
            trail,
            (IMessageRegistry) serviceProvider.GetService(typeof(IMessageRegistry))!,
            (IAuditOutcomeMapper) serviceProvider.GetService(typeof(IAuditOutcomeMapper))!,
            (TimeProvider) serviceProvider.GetService(typeof(TimeProvider))!,
            (IAuditActorResolver?) serviceProvider.GetService(typeof(IAuditActorResolver)),
            serviceProvider);
    }
}
