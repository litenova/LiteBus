using System;
using System.Collections.Generic;
using System.Linq;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Composition;
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
    ///     The configuration callbacks invoked while the messaging module is built, in registration order.
    /// </summary>
    private readonly List<Action<MessageModuleBuilder>> _builders = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModule" /> class.
    /// </summary>
    /// <param name="builder">The configuration action for the message module.</param>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="builder" /> is <see langword="null" />.</exception>
    public MessageModule(Action<MessageModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builders.Add(builder);
    }

    /// <summary>
    ///     Runs an additional configuration callback on this module's builder when the module builds.
    /// </summary>
    /// <param name="builderAction">The additional messaging configuration callback.</param>
    /// <param name="registerChild">Registers child modules with the module registry; the messaging module declares none.</param>
    internal void Configure(Action<MessageModuleBuilder> builderAction, Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(builderAction);
        ArgumentNullException.ThrowIfNull(registerChild);
        _builders.Add(builderAction);
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
        var refusalCatalog = configuration.GetOrCreateContext(() => RefusalCatalog.CreateDefault());
        var startIndex = messageRegistry.Handlers.Count;

        // Configure the message module using the builder.
        var moduleBuilder = new MessageModuleBuilder(messageRegistry, messageContractRegistry, refusalCatalog);

        foreach (var configure in _builders)
        {
            configure(moduleBuilder);
        }

        // Applied before anything mediates, and process-wide, because an ActivitySource and a Meter are.
        // Always assigned, so a composition that configures no telemetry resets the process-wide options to the
        // defaults rather than silently inheriting a previous composition's settings.
        MediationTelemetry.Options = moduleBuilder.Telemetry ?? new MediationTelemetryOptions();

        // Register core messaging services.
        RegisterMessagingServices(
            configuration,
            messageRegistry,
            messageContractRegistry,
            moduleBuilder.TimeProvider,
            moduleBuilder.Serializer,
            moduleBuilder.SerializerLifetime,
            moduleBuilder.SerializerOptionsProvider,
            moduleBuilder.MessageSampleFactory,
            moduleBuilder.MessageSamples,
            moduleBuilder.FaultRefusalClassifiers);
        RegisterRefusalCatalog(configuration, refusalCatalog);
        ModuleRegistration.RegisterNewHandlers(configuration, messageRegistry, startIndex);

        foreach (var step in moduleBuilder.BuildSteps)
        {
            step(configuration);
        }

        // Deferred past the build steps so convention-supplied refusal declarations are harvested, and before the
        // composition checks so the refusals section reports every harvested entry.
        configuration.RegisterCompositionValidation(
            () => RefusalCatalog.HarvestDeclarations(messageRegistry, refusalCatalog));

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

        // Deferred for the same reason. The model is built once here and shared by every check, so ten checks
        // cost one traversal of the registry. It is built even when no check was registered, so a misregistered
        // section contributor still fails composition instead of surfacing on first resolve.
        var compositionChecks = moduleBuilder.CompositionChecks;
        configuration.RegisterCompositionValidation(() => RunCompositionChecks(
            messageRegistry,
            messageContractRegistry,
            configuration.DependencyRegistry,
            compositionChecks));
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
    /// <param name="moduleBuilder">The messaging builder, read for the declaration configuration.</param>
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
        summary.CompositionChecks = moduleBuilder.CompositionChecks.Count;

        foreach (var (handlerType, closures) in reader.OpenGenericClosures)
        {
            summary.RecordOpenGeneric(handlerType.Name, closures.Count);
        }

        ReportDuplicateStageCoverage(summary, reader);

        foreach (var requirement in moduleBuilder.RequiredDeclarations)
        {
            summary.RecordRequiredDeclaration($"{requirement.ValueType.Name} of {requirement.ScopeDescription}");
        }
    }

    /// <summary>
    ///     Reports every handler type covering one message's stage twice, once directly and once through a base type.
    /// </summary>
    /// <param name="summary">The summary receiving the duplicate coverage entries.</param>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <remarks>
    ///     Main handlers and refusal mappers are excluded: one main handler answers a command and one mapper converts
    ///     a refusal, with the direct registration preferred, so a handler present in both sets still runs once.
    ///     Pre-stage, post, error, and completion entries in both sets all run, which is what makes them worth naming.
    /// </remarks>
    private static void ReportDuplicateStageCoverage(LiteBusCompositionSummary summary, IMessageReader reader)
    {
        foreach (var descriptor in reader)
        {
            foreach (var stage in PipelineContracts.StagesInOrder)
            {
                ReportDuplicateRole(
                    summary,
                    descriptor.MessageType,
                    descriptor.PreStageHandlers.Where(handler => handler.Stage == stage),
                    descriptor.IndirectPreStageHandlers.Where(handler => handler.Stage == stage));
            }

            ReportDuplicateRole(summary, descriptor.MessageType, descriptor.PostHandlers, descriptor.IndirectPostHandlers);
            ReportDuplicateRole(summary, descriptor.MessageType, descriptor.ErrorHandlers, descriptor.IndirectErrorHandlers);
            ReportDuplicateRole(
                summary,
                descriptor.MessageType,
                descriptor.CompletionHandlers,
                descriptor.IndirectCompletionHandlers);
        }
    }

    /// <summary>
    ///     Reports the handler types present in both the direct and indirect sets of one role for one message.
    /// </summary>
    /// <typeparam name="TDescriptor">The descriptor kind for the role.</typeparam>
    /// <param name="summary">The summary receiving the duplicate coverage entries.</param>
    /// <param name="messageType">The message whose role is being compared.</param>
    /// <param name="direct">The entries closed over the message itself.</param>
    /// <param name="indirect">The entries closed over a base type of the message.</param>
    /// <remarks>
    ///     Handler types are compared by open generic definition, so an open generic closed over both the base
    ///     interface and the concrete message reports once. A handler covering a role three times still reports once;
    ///     the entry names the first contract on each side, which is enough to find the registrations.
    /// </remarks>
    private static void ReportDuplicateRole<TDescriptor>(
        LiteBusCompositionSummary summary,
        Type messageType,
        IEnumerable<TDescriptor> direct,
        IEnumerable<TDescriptor> indirect)
        where TDescriptor : IHandlerDescriptor
    {
        Dictionary<Type, Type> directContracts = [];

        foreach (var entry in direct)
        {
            directContracts.TryAdd(CoverageKey(entry.HandlerType), entry.ContractType);
        }

        foreach (var entry in indirect)
        {
            var key = CoverageKey(entry.HandlerType);

            if (directContracts.TryGetValue(key, out var directContract))
            {
                summary.RecordDuplicateStageCoverage(key, messageType, directContract, entry.ContractType);
                directContracts.Remove(key);
            }
        }
    }

    /// <summary>
    ///     Normalizes a handler type for duplicate comparison.
    /// </summary>
    /// <param name="handlerType">The handler type recorded on a descriptor.</param>
    /// <returns>The open generic definition for a closed generic, and the type itself otherwise.</returns>
    private static Type CoverageKey(Type handlerType)
    {
        return handlerType.IsGenericType ? handlerType.GetGenericTypeDefinition() : handlerType;
    }

    /// <summary>
    ///     Runs every application composition check over one model of everything the host composed.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="contracts">The contract registry holding every registered message contract.</param>
    /// <param name="dependencyRegistry">The dependency registry holding every composed descriptor.</param>
    /// <param name="checks">The checks the application registered through <c>ValidateComposition</c>.</param>
    /// <remarks>
    ///     Checks run in registration order and the first to throw ends composition. Aggregating them would report
    ///     more at once, but a check is free to name every offender itself, and a convention violation is usually one
    ///     fix rather than a list of independent ones.
    /// </remarks>
    private static void RunCompositionChecks(
        IMessageReader reader,
        MessageContractRegistry contracts,
        IDependencyRegistry dependencyRegistry,
        IReadOnlyList<Action<ICompositionModel>> checks)
    {
        var model = CompositionModel.CreateForValidation(reader, contracts, [.. dependencyRegistry]);

        foreach (var check in checks)
        {
            check(model);
        }
    }

    /// <summary>
    ///     Registers core messaging services with the dependency registry.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageRegistry">The message registry instance.</param>
    /// <param name="messageContractRegistry">The message contract registry instance.</param>
    /// <param name="timeProvider">The optional time provider registered for messaging services.</param>
    /// <param name="messageSerializer">The serializer instance or implementation type, or null for the built-in one.</param>
    /// <param name="messageSerializerLifetime">The lifetime a serializer implementation type is registered with.</param>
    /// <param name="serializerOptionsProvider">The options, provider instance, or provider type, or null for the built-in one.</param>
    /// <param name="messageSampleFactory">The sample factory instance or implementation type, or null for the built-in one.</param>
    /// <param name="messageSamples">The per-type samples registered for the built-in factory.</param>
    /// <param name="faultRefusalClassifiers">The classifier implementation types and instances, in registration order.</param>
    private static void RegisterMessagingServices(
        IModuleConfiguration configuration,
        IMessageRegistry messageRegistry,
        MessageContractRegistry messageContractRegistry,
        TimeProvider? timeProvider,
        object? messageSerializer,
        InstanceLifetime messageSerializerLifetime,
        object? serializerOptionsProvider,
        object? messageSampleFactory,
        MessageSampleRegistry messageSamples,
        IReadOnlyList<object> faultRefusalClassifiers)
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

        // Resolvable at runtime as well as inside a composition check, so a host logs the exported snapshot and
        // tests assert over it. Built by a singleton factory, so the snapshot is taken on first resolve, after
        // every module has registered what it owns, and the effective port implementations resolve from the
        // container that answers them at mediation.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICompositionModel),
            provider => CompositionModel.CreateResolved(
                messageRegistry,
                messageContractRegistry,
                [.. configuration.DependencyRegistry],
                provider),
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

        RegisterMessageSerializer(configuration, messageSerializer, messageSerializerLifetime, serializerOptionsProvider);
        RegisterMessageSampleFactory(configuration, messageSampleFactory, messageSamples);

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

        // The key resolver reads a declaration resolved once at registration, so it holds no state and a singleton is
        // correct. The store itself is application-supplied and is not registered here.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IdempotencyKeyResolver),
            new IdempotencyKeyResolver(new MessageMetadataAccessor(messageRegistry))));

        // Register message mediator as transient.
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IMessageMediator),
            typeof(MessageMediator)));

        RegisterFaultRefusalClassifiers(configuration, faultRefusalClassifiers);
    }

    /// <summary>
    ///     Registers the fault refusal classifiers as a multi-registration collection resolved in registration order.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="faultRefusalClassifiers">The classifier implementation types and instances, in registration order.</param>
    /// <remarks>
    ///     Each entry is registered as one collection item rather than as the service itself, so several classifiers
    ///     coexist and the strategies resolve them as an ordered enumerable. With no entry nothing is registered, and
    ///     every fault keeps the behavior it had before classifiers existed.
    /// </remarks>
    private static void RegisterFaultRefusalClassifiers(
        IModuleConfiguration configuration,
        IReadOnlyList<object> faultRefusalClassifiers)
    {
        foreach (var entry in faultRefusalClassifiers)
        {
            if (entry is Type classifierType)
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(IFaultRefusalClassifier),
                    classifierType,
                    InstanceLifetime.Singleton));
            }
            else
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(IFaultRefusalClassifier),
                    entry));
            }
        }
    }

    /// <summary>
    ///     Registers the shared refusal catalog for transports and the composition model.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="refusalCatalog">The catalog the messaging builder collected registrations into.</param>
    /// <remarks>
    ///     The catalog instance also contributes the <c>refusals</c> composition section, so the export names every
    ///     entry with its owner.
    /// </remarks>
    private static void RegisterRefusalCatalog(IModuleConfiguration configuration, RefusalCatalog refusalCatalog)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IRefusalCatalog),
            refusalCatalog));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IRefusalReader),
            refusalCatalog));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IRefusalWriter),
            refusalCatalog));

        configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
            typeof(ICompositionSectionContributor),
            refusalCatalog));
    }

    /// <summary>
    ///     Registers the serializer that produces and reads persisted message payloads.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageSerializer">The serializer instance or implementation type, or null for the built-in one.</param>
    /// <param name="lifetime">The lifetime an implementation type is registered with.</param>
    /// <param name="serializerOptionsProvider">The options, provider instance, or provider type, or null for the built-in one.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     The builder held a value that is not a serializer, or held both a custom serializer and custom options.
    /// </exception>
    /// <remarks>
    ///     Falling back to <see cref="SystemTextJsonMessageSerializer" /> built from the resolved options provider
    ///     keeps the default wire format unchanged for applications that never state one. A custom serializer together
    ///     with custom options fails composition, because two payload formats would apply with nothing saying which.
    /// </remarks>
    private static void RegisterMessageSerializer(
        IModuleConfiguration configuration,
        object? messageSerializer,
        InstanceLifetime lifetime,
        object? serializerOptionsProvider)
    {
        if (messageSerializer is not null && serializerOptionsProvider is not null)
        {
            throw new LiteBusConfigurationException(
                "The composition sets both a custom message serializer and custom serializer options. "
                + "A custom serializer carries its own format, so the options would never apply; state the wire "
                + "format once, either as a serializer or as options.");
        }

        RegisterSerializerOptionsProvider(configuration, serializerOptionsProvider);

        switch (messageSerializer)
        {
            case null:
                configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
                    typeof(IMessageSerializer),
                    provider => new SystemTextJsonMessageSerializer(ResolveSerializerOptions(provider))));
                break;

            case Type implementationType:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageSerializer),
                    implementationType,
                    lifetime));
                break;

            case IMessageSerializer instance:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageSerializer),
                    instance));
                break;

            default:
                throw new LiteBusConfigurationException(
                    $"The configured message serializer '{messageSerializer.GetType().Name}' is neither an "
                    + $"{nameof(IMessageSerializer)} instance nor an implementation type.");
        }
    }

    /// <summary>
    ///     Registers the JSON serializer options provider every durable feature serializes message payloads with.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="serializerOptionsProvider">The options, provider instance, or provider type, or null for the built-in one.</param>
    /// <exception cref="LiteBusConfigurationException">The builder held a value that is not serializer options.</exception>
    /// <remarks>
    ///     Always registered, so the composition model names the effective source and the default serializer factory
    ///     resolves it. Fixed options wrap in a constant provider, which is what the simple
    ///     <c>UseSerializerOptions</c> overload registers over.
    /// </remarks>
    private static void RegisterSerializerOptionsProvider(IModuleConfiguration configuration, object? serializerOptionsProvider)
    {
        switch (serializerOptionsProvider)
        {
            case null:
                configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
                    typeof(IMessageJsonSerializerOptionsProvider),
                    typeof(DefaultMessageJsonSerializerOptionsProvider)));
                break;

            case System.Text.Json.JsonSerializerOptions options:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageJsonSerializerOptionsProvider),
                    new FixedMessageJsonSerializerOptionsProvider(options)));
                break;

            case Type implementationType:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageJsonSerializerOptionsProvider),
                    implementationType,
                    InstanceLifetime.Singleton));
                break;

            case IMessageJsonSerializerOptionsProvider instance:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageJsonSerializerOptionsProvider),
                    instance));
                break;

            default:
                throw new LiteBusConfigurationException(
                    $"The configured serializer options '{serializerOptionsProvider.GetType().Name}' are neither "
                    + "'JsonSerializerOptions', nor an 'IMessageJsonSerializerOptionsProvider' instance, nor an "
                    + "implementation type.");
        }
    }

    /// <summary>
    ///     Resolves the JSON serializer options the default serializer serializes message payloads with.
    /// </summary>
    /// <param name="provider">The service provider holding the composed options provider.</param>
    /// <returns>The shared options.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">No options provider is registered.</exception>
    private static System.Text.Json.JsonSerializerOptions ResolveSerializerOptions(IServiceProvider provider)
    {
        return ((IMessageJsonSerializerOptionsProvider?) provider.GetService(
                typeof(IMessageJsonSerializerOptionsProvider))
            ?? throw new LiteBusDependencyResolutionException(typeof(IMessageJsonSerializerOptionsProvider))).Options;
    }

    /// <summary>
    ///     Registers the sample factory host validation round-trip checks sample message contracts with.
    /// </summary>
    /// <param name="configuration">The module configuration.</param>
    /// <param name="messageSampleFactory">The sample factory instance or implementation type, or null for the built-in one.</param>
    /// <param name="messageSamples">The per-type samples registered for the built-in factory.</param>
    /// <exception cref="LiteBusConfigurationException">
    ///     The builder held a value that is not a sample factory, or held both per-type samples and a custom
    ///     factory that would silently ignore them.
    /// </exception>
    /// <remarks>
    ///     A default factory fed with registered samples still registers through
    ///     <see cref="DependencyDescriptor.ForDefaultPort(Type, object)" />, so the composition model reports the
    ///     port as the default. The samples registry joins the composition sections only when samples exist, so
    ///     compositions without samples export no empty section.
    /// </remarks>
    private static void RegisterMessageSampleFactory(
        IModuleConfiguration configuration,
        object? messageSampleFactory,
        MessageSampleRegistry messageSamples)
    {
        if (messageSampleFactory is not null && messageSamples.Count > 0)
        {
            throw new LiteBusConfigurationException(
                "The composition registers per-type samples through 'RegisterSample' and replaces the message "
                + "sample factory. A custom factory never consults the registered samples, so they would be "
                + "silently ignored; move the samples into the custom factory or remove the replacement.");
        }

        if (messageSamples.Count > 0)
        {
            configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
                typeof(IMessageSampleFactory),
                new DefaultMessageSampleFactory(messageSamples.Snapshot())));
            configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                typeof(ICompositionSectionContributor),
                messageSamples));
            return;
        }

        switch (messageSampleFactory)
        {
            case null:
                configuration.DependencyRegistry.Register(DependencyDescriptor.ForDefaultPort(
                    typeof(IMessageSampleFactory),
                    typeof(DefaultMessageSampleFactory)));
                break;

            case Type implementationType:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageSampleFactory),
                    implementationType,
                    InstanceLifetime.Singleton));
                break;

            case IMessageSampleFactory instance:
                configuration.DependencyRegistry.Register(new DependencyDescriptor(
                    typeof(IMessageSampleFactory),
                    instance));
                break;

            default:
                throw new LiteBusConfigurationException(
                    $"The configured message sample factory '{messageSampleFactory.GetType().Name}' is neither an "
                    + $"{nameof(IMessageSampleFactory)} instance nor an implementation type.");
        }
    }
}
