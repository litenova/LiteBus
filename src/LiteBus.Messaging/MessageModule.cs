using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Idempotency;
using LiteBus.Messaging.Mediator;
using LiteBus.Messaging.Pipeline;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging;

/// <summary>
///     Module for configuring messaging infrastructure components.
///     This is a foundational module that other modules depend on.
/// </summary>
public sealed class MessageModule : IModule
{
    /// <summary>
    ///     The configuration callback invoked while the messaging module is built.
    /// </summary>
    private readonly Action<MessageModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the message module.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public MessageModule(Action<MessageModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        if (!configuration.DependencyRegistry.Any(static descriptor =>
                descriptor.DependencyType == typeof(IMessageDispatchScopeFactory)))
        {
            throw new ModuleCompositionException(
                "Message dispatch requires an IMessageDispatchScopeFactory. " +
                "Use a supported container adapter or explicitly register RootMessageDispatchScopeFactory in a custom host.");
        }

        // Create or get the message registry shared by all messaging-related modules in this configuration.
        var messageRegistry = configuration.GetOrCreateContext<IMessageRegistry>(() => new MessageRegistry());
        var messageContractRegistry = configuration.GetOrCreateContext(() => new MessageContractRegistry());
        var startIndex = messageRegistry.Handlers.Count;

        // Configure the message module using the builder.
        var moduleBuilder = new MessageModuleBuilder(messageRegistry, messageContractRegistry);
        _builder(moduleBuilder);

        // Applied before anything mediates, and process-wide, because an ActivitySource and a Meter are.
        if (moduleBuilder.Telemetry is not null)
        {
            MediationTelemetry.Options = moduleBuilder.Telemetry;
        }

        // Register core messaging services.
        RegisterMessagingServices(
            configuration,
            messageRegistry,
            messageContractRegistry,
            moduleBuilder.TimeProvider,
            moduleBuilder.AuditOutcomeMapper,
            moduleBuilder.AuditTrail,
            moduleBuilder.AuditTrailLifetime,
            moduleBuilder.AuditActorResolver,
            moduleBuilder.AuditActorResolverLifetime);
        RegisterNewHandlers(configuration, messageRegistry, startIndex);

        // Published for the axis modules, which build after this one because they declare IRequires<MessageModule>.
        // AddAuditing decides the axes here so the consumer does not repeat the decision on each axis builder.
        if (moduleBuilder.Auditing is not null)
        {
            configuration.SetContext(moduleBuilder.Auditing);

            // A trail with no axis selected is the whole feature wired and inert, which no probe can report as
            // unhealthy because nothing is wrong at runtime: no message is ever audited, so nothing ever fails. It is
            // only detectable here, where the intent to audit is visible next to the absence of anything to audit.
            if (!moduleBuilder.Auditing.AnyAxis)
            {
                throw new AuditConfigurationException(
                    "AddAuditing configured the audit trail but selected no axis, so no message would ever produce a "
                    + "record. Call ForCommands, ForQueries, ForEvents, or ForAllAxes to say what to audit.");
            }
        }

        // Registered now and filled in after every module has built, because none of the counts exist yet. Sharing
        // the instance is what lets the axis modules record their own message counts into it.
        var summary = configuration.GetOrCreateContext(() => new LiteBusCompositionSummary());

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(LiteBusCompositionSummary),
            summary));

        configuration.RegisterCompositionValidation(() => Summarize(
            summary,
            messageRegistry,
            moduleBuilder));

        if (moduleBuilder.ExplicitOpenGenericsRequired)
        {
            configuration.RegisterCompositionValidation(
                () => ThrowIfOpenGenericsWereScanned(messageRegistry));
        }

        // Deferred because this module is foundational: the commands and queries the requirements apply to are
        // registered by modules that build after it.
        if (moduleBuilder.RequiredDeclarations.Count > 0)
        {
            var required = moduleBuilder.RequiredDeclarations;
            configuration.RegisterCompositionValidation(
                () => RequiredDeclarationValidator.Validate(messageRegistry, required));
        }

        // Deferred for the same reason. The catalog is built once here and shared by every check, so ten conventions
        // cost one traversal of the registry.
        if (moduleBuilder.CompositionChecks.Count > 0)
        {
            var checks = moduleBuilder.CompositionChecks;
            configuration.RegisterCompositionValidation(() => RunCompositionChecks(messageRegistry, checks));
        }
    }

    /// <summary>
    ///     Fails composition when an open generic pipeline handler arrived through an assembly scan.
    /// </summary>
    /// <param name="reader">The registry holding the scanned open generic handlers.</param>
    /// <exception cref="PipelineContractException">
    ///     One or more open generic handlers were discovered by scanning rather than named.
    /// </exception>
    /// <remarks>
    ///     Every offender is named, with the registration line that fixes it, because a team turning this on for an
    ///     existing codebase has several and fixing them one composition failure at a time would make the switch
    ///     unusable. Internal rather than private so the check can be tested against a registry directly, without a
    ///     host whose scanned assembly would also pick up every other type in it.
    /// </remarks>
    internal static void ThrowIfOpenGenericsWereScanned(IMessageReader reader)
    {
        var scanned = reader.ScannedOpenGenericHandlers
            .Where(handler => reader.OpenGenericClosures.ContainsKey(handler))
            .OrderBy(handler => handler.Name, StringComparer.Ordinal)
            .ToList();

        if (scanned.Count == 0)
        {
            return;
        }

        var lines = scanned.Select(handler =>
            $"  {handler.Name} closes over {reader.OpenGenericClosures[handler].Count} messages; register it with "
            + $"Register(typeof({handler.Name[..handler.Name.IndexOf('`', StringComparison.Ordinal)]}<>))");

        throw new PipelineContractException(
            "RequireExplicitOpenGenerics is on and one or more open generic pipeline handlers were discovered by "
            + "assembly scanning, so they insert a stage into every message they fit with no registration line to "
            + "review:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    ///     Fills in the composition summary once every module has built.
    /// </summary>
    /// <param name="summary">The summary registered during build.</param>
    /// <param name="reader">The registry holding every registered message and open generic closure.</param>
    /// <param name="moduleBuilder">The messaging builder, read for the audit and declaration configuration.</param>
    /// <remarks>
    ///     Runs as a composition validation rather than during build, because the message count, the axis counts and
    ///     the open generic closures are all only complete after the last axis module has registered its types.
    /// </remarks>
    private static void Summarize(
        LiteBusCompositionSummary summary,
        IMessageReader reader,
        MessageModuleBuilder moduleBuilder)
    {
        summary.MessageCount = reader.Count;
        summary.AuditingEnabled = moduleBuilder.Auditing?.AnyAxis == true;
        summary.AuditActorResolverRegistered = moduleBuilder.AuditActorResolver is not null;
        summary.CompositionChecks = moduleBuilder.CompositionChecks.Count;

        summary.AuditTrail = moduleBuilder.AuditTrail switch
        {
            Type trailType => $"{trailType.Name} ({moduleBuilder.AuditTrailLifetime})",
            null => null,
            var instance => $"{instance.GetType().Name} (Singleton)"
        };

        foreach (var (handlerType, closures) in reader.OpenGenericClosures)
        {
            summary.RecordOpenGeneric(handlerType.Name, closures.Count);
        }

        foreach (var requirement in moduleBuilder.RequiredDeclarations)
        {
            summary.RecordRequiredDeclaration($"{requirement.ValueType.Name} of {requirement.ScopeDescription}");
        }
    }

    /// <summary>
    ///     Runs every application composition check over one catalog of registered messages.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="checks">The checks the application registered through <c>ValidateComposition</c>.</param>
    /// <remarks>
    ///     Checks run in registration order and the first to throw ends composition. Aggregating them would report
    ///     more at once, but a check is free to name every offender itself, and a convention violation is usually one
    ///     fix rather than a list of independent ones.
    /// </remarks>
    private static void RunCompositionChecks(IMessageReader reader, IReadOnlyList<Action<IMessageCatalog>> checks)
    {
        var catalog = new MessageCatalog(reader);

        foreach (var check in checks)
        {
            check(catalog);
        }
    }

    /// <summary>
    ///     Registers core messaging services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry instance.</param>
    /// <param name="messageContractRegistry">The message contract registry instance.</param>
    /// <param name="timeProvider">The optional time provider registered for messaging services.</param>
    /// <param name="auditOutcomeMapper">The optional audit outcome mapper registered for the audit writer.</param>
    /// <param name="auditTrail">The audit trail registered through the builder, when the application supplied one there.</param>
    /// <param name="auditTrailLifetime">The lifetime an audit trail implementation type is registered with.</param>
    /// <param name="auditActorResolver">The audit actor resolver registered through the builder, when there is one.</param>
    /// <param name="auditActorResolverLifetime">The lifetime an actor resolver implementation type is registered with.</param>
    private static void RegisterMessagingServices(
        IModuleConfiguration configuration,
        IMessageRegistry messageRegistry,
        MessageContractRegistry messageContractRegistry,
        TimeProvider? timeProvider,
        IAuditOutcomeMapper? auditOutcomeMapper,
        object? auditTrail,
        InstanceLifetime auditTrailLifetime,
        object? auditActorResolver,
        InstanceLifetime auditActorResolverLifetime)
    {
        // Register message registry as singleton.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageRegistry),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageReader),
            messageRegistry));

        // Resolvable at runtime as well as inside a composition check, so an application can render its audit
        // catalogue or its authorization matrix from the declarations rather than maintaining it by hand. Built by a
        // singleton factory, so the snapshot is taken on first resolve, after every module has registered its
        // messages.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageCatalog),
            _ => new MessageCatalog(messageRegistry),
            InstanceLifetime.Singleton));

        // The read-only view applications use to read their own declarations, so a generic guard does not have to
        // navigate descriptors to find the metadata bag.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMetadataAccessor),
            new MessageMetadataAccessor(messageRegistry)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageWriter),
            messageRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageContractRegistry),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IContractReader),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IContractWriter),
            messageContractRegistry));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageSerializer),
            typeof(SystemTextJsonMessageSerializer)));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(TimeProvider),
            timeProvider ?? TimeProvider.System));

        // The execution context of the mediation in flight, so a handler can take it as an ordinary constructor
        // dependency instead of reaching for the AsyncLocal static. The mediator opens the ambient scope before it
        // creates the dispatch scope, and there is one dispatch scope per mediation, so a scoped factory over the
        // ambient value resolves the right context and the container's per-scope cache holds exactly one.
        // AmbientExecutionContext stays available for code that runs outside dependency injection.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IExecutionContext),
            static _ => AmbientExecutionContext.Current,
            InstanceLifetime.Scoped));

        // The audit scope is stateless: it reads and writes the ambient execution context, so a singleton is correct
        // and remains safe under concurrency because two mediations never share an execution context.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditScope),
            new AmbientAuditScope()));

        // Applications supply their own mapper to record a refusal exception as a denial rather than a failure.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditOutcomeMapper),
            auditOutcomeMapper ?? new DefaultAuditOutcomeMapper()));

        RegisterAuditTrail(configuration, auditTrail, auditTrailLifetime);
        RegisterAuditActorResolver(configuration, auditActorResolver, auditActorResolverLifetime);

        // Registered through a factory rather than by type on purpose. The writer needs an IAuditTrail that only an
        // application auditing its messages registers, and a by-type registration makes a container running
        // ValidateOnBuild fail at startup for every application that is not auditing at all. The factory defers the
        // lookup to the first audited mediation and names the fix when it is missing; the litebus.audit.trail probe
        // reports it earlier still.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditRecordWriter),
            CreateAuditRecordWriter,
            InstanceLifetime.Scoped));

        // The key resolver reads a declaration resolved once at registration, so it holds no state and a singleton is
        // correct. The store itself is application-supplied and is not registered here.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IdempotencyKeyResolver),
            new IdempotencyKeyResolver(new MessageMetadataAccessor(messageRegistry))));

        // Register message mediator as transient.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMediator),
            typeof(MessageMediator)));
    }

    /// <summary>
    ///     Registers the audit trail the builder was given, when it was given one.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="auditTrail">The trail instance or implementation type, or null when the application registers it itself.</param>
    /// <param name="lifetime">The lifetime an implementation type is registered with; an instance is always a singleton.</param>
    /// <remarks>
    ///     An application may register the trail with its own container instead, which is what the
    ///     <c>litebus.audit.trail</c> probe checks for. Configuring it here keeps the whole feature on one builder.
    /// </remarks>
    private static void RegisterAuditTrail(
        IModuleConfiguration configuration,
        object? auditTrail,
        InstanceLifetime lifetime)
    {
        switch (auditTrail)
        {
            case null:
                return;

            case Type trailType:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IAuditTrail),
                    trailType,
                    lifetime));
                return;

            default:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IAuditTrail),
                    auditTrail));
                return;
        }
    }

    /// <summary>
    ///     Registers the audit actor resolver the builder was given, when it was given one.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="auditActorResolver">
    ///     The resolver instance or implementation type, or null when the application registers it itself or does not
    ///     attribute its records.
    /// </param>
    /// <param name="lifetime">The lifetime an implementation type is registered with; an instance is always a singleton.</param>
    /// <remarks>
    ///     A missing resolver is not a composition failure. Auditing without attribution is a poorer trail rather than
    ///     a broken one, and an application may register the resolver with its own container; the
    ///     <c>litebus.audit.trail</c> probe reports the gap either way.
    /// </remarks>
    private static void RegisterAuditActorResolver(
        IModuleConfiguration configuration,
        object? auditActorResolver,
        InstanceLifetime lifetime)
    {
        switch (auditActorResolver)
        {
            case null:
                return;

            case Type resolverType:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IAuditActorResolver),
                    resolverType,
                    lifetime));
                return;

            default:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IAuditActorResolver),
                    auditActorResolver));
                return;
        }
    }

    /// <summary>
    ///     Creates the audit record writer, reporting a missing trail as configuration rather than a null reference.
    /// </summary>
    /// <param name="serviceProvider">The scope the writer is being resolved from.</param>
    /// <returns>The writer bound to the registered trail.</returns>
    /// <exception cref="AuditConfigurationException">
    ///     Auditing is enabled but no <see cref="IAuditTrail" /> is registered.
    /// </exception>
    private static AuditRecordWriter CreateAuditRecordWriter(IServiceProvider serviceProvider)
    {
        var trail = (IAuditTrail?) serviceProvider.GetService(typeof(IAuditTrail))
                    ?? throw new AuditConfigurationException(
                        "Auditing is enabled but no IAuditTrail is registered, so audit records cannot be written. "
                        + "Register one with UseAuditTrail on the messaging module builder, or with the application "
                        + "container. The litebus.audit.trail diagnostic check reports this before the first message "
                        + "arrives.");

        return new AuditRecordWriter(
            trail,
            (IMessageRegistry) serviceProvider.GetService(typeof(IMessageRegistry))!,
            (IAuditOutcomeMapper) serviceProvider.GetService(typeof(IAuditOutcomeMapper))!,
            (TimeProvider) serviceProvider.GetService(typeof(TimeProvider))!,
            (IAuditActorResolver?) serviceProvider.GetService(typeof(IAuditActorResolver)));
    }

    /// <summary>
    ///     Registers handler types that were added to the message registry during module building.
    /// </summary>
    /// <param name="configuration">The module configuration to register handlers with.</param>
    /// <param name="messageRegistry">The message registry containing the handlers.</param>
    /// <param name="startIndex">The index from which to start registering new handlers.</param>
    private static void RegisterNewHandlers(IModuleConfiguration configuration, IMessageRegistry messageRegistry, int startIndex)
    {
        var newHandlers = messageRegistry.Handlers.Skip(startIndex);

        foreach (var handlerDescriptor in newHandlers)
        {
            var handlerType = handlerDescriptor.HandlerType;

            if (handlerType is { IsClass: true, IsAbstract: false })
            {
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    handlerType,
                    handlerType,
                    InstanceLifetime.Scoped));
            }
        }
    }
}
