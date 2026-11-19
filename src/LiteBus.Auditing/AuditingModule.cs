using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using LiteBus.Auditing.Capture;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Hashing;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Segments;
using LiteBus.Auditing.Verification;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Auditing;

/// <summary>
///     Registers the auditing feature: the capture entry trail, the hash chain engine, and its background work.
/// </summary>
/// <remarks>
///     <para>
///         Reached through <c>AddAuditing</c> on <see cref="ModuleRegistryExtensions" />. Calling that twice
///         configures one module instead of registering a second one, so several application modules can add to one
///         composition.
///         A storage package records its open-generic trail and chain store through its extension on
///         <see cref="AuditingModuleBuilder" />; this module closes them over the configured document type and
///         registers every engine component the chain runs on. A store or anchor that needs settings registers
///         its settings module as a child through <see cref="AuditingModuleBuilder.RegisterChildModule" />, so
///         the settings compose inside <c>AddAuditing</c> and build with it.
///     </para>
///     <para>
///         Composition validates the builder capture and link pair through
///         <see cref="AuditCaptureResolver.ResolveDefault" />, which also selects
///         <see cref="AuditCapture.AfterCommitThroughOutbox" /> when the store cannot join the business
///         transaction. The selection is recorded on the registered <see cref="AuditingOptions" /> and reported
///         in the composition model through <see cref="AuditingCompositionSection" />, never applied silently.
///     </para>
///     <para>
///         The engine implementations are internal to this assembly, so containers cannot construct them. The
///         module registers each one through a factory that resolves its constructor arguments from the provider
///         and constructs the closed type. Public application and storage types are registered by type so the
///         container constructs them.
///     </para>
/// </remarks>
public sealed class AuditingModule : ICompositeModule
{
    /// <summary>
    ///     The module builder callback invoked during <see cref="DeclareChildren" />.
    /// </summary>
    private readonly Action<AuditingModuleBuilder> _configure;

    /// <summary>
    ///     The builder populated when child modules are declared, and read again when the module builds.
    /// </summary>
    private AuditingModuleBuilder? _builder;

    /// <summary>
    ///     The child modules already declared through the module registry, compared by reference.
    /// </summary>
    private readonly List<IModule> _declaredChildren = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditingModule" /> class.
    /// </summary>
    /// <param name="configure">The module configuration action.</param>
    public AuditingModule(Action<AuditingModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _configure = configure;
    }

    /// <summary>
    ///     Runs an additional configuration callback on the existing auditing builder and declares new child modules.
    /// </summary>
    /// <param name="builderAction">The additional auditing configuration action.</param>
    /// <param name="registerChild">Registers each newly declared child module with the module registry.</param>
    internal void Configure(Action<AuditingModuleBuilder> builderAction, Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(builderAction);
        ArgumentNullException.ThrowIfNull(registerChild);

        var moduleBuilder = EnsureBuilder();

        builderAction(moduleBuilder);
        DeclareNewChildren(moduleBuilder, registerChild);
    }

    /// <inheritdoc />
    public void DeclareChildren(Action<IModule> registerChild)
    {
        ArgumentNullException.ThrowIfNull(registerChild);

        DeclareNewChildren(EnsureBuilder(), registerChild);
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = EnsureBuilder();

        ThrowIfChildrenWereNeverDeclared(moduleBuilder);

        var options = moduleBuilder.Options;
        var capabilities = RequireStoreCapabilities(options);
        var resolution = AuditCaptureResolver.ResolveDefault(options.Capture, capabilities);

        options.Capture = resolution.Effective;

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(AuditingOptions), options));

        var documentType = options.Chain?.DocumentType ?? options.StoreDefaultDocumentType;

        if (options.Chain is { } chain)
        {
            BuildChain(configuration, options, chain, documentType);
        }
        else
        {
            BuildTrailOnly(configuration, options, documentType);
        }

        RegisterEntryTrail(configuration, options);

        if (options.Chain is not null &&
            options.Capture is AuditCapture.WithChangeCapture { Link: AuditLink.AfterCommit })
        {
            RegisterAfterCommitLinking(configuration);
        }

        var section = new AuditingCompositionSection(options, resolution, DescribeCheckpointPolicy(options.Chain));

        configuration.DependencyRegistry.RegisterCollection(
            DependencyDescriptor.ForCollection(typeof(ICompositionSectionContributor), section));
    }

    /// <summary>
    ///     Gets the module builder, running the stored configuration action on first use.
    /// </summary>
    /// <returns>The populated module builder.</returns>
    /// <remarks>
    ///     The registry declares children during <see cref="IModuleRegistry.Register" />, so in a composed host
    ///     the builder already exists by the time the module builds. Running the action here as well keeps direct
    ///     construction working for callers that build the module without a registry.
    /// </remarks>
    private AuditingModuleBuilder EnsureBuilder()
    {
        if (_builder is null)
        {
            var moduleBuilder = new AuditingModuleBuilder();
            _configure(moduleBuilder);
            _builder = moduleBuilder;
        }

        return _builder;
    }

    /// <summary>
    ///     Declares the builder child modules that were not declared before.
    /// </summary>
    /// <param name="moduleBuilder">The populated module builder holding the queued child modules.</param>
    /// <param name="registerChild">Registers each newly declared child module with the module registry.</param>
    private void DeclareNewChildren(AuditingModuleBuilder moduleBuilder, Action<IModule> registerChild)
    {
        foreach (var childModule in moduleBuilder.CollectSubModules())
        {
            if (IsAlreadyDeclared(childModule))
            {
                continue;
            }

            registerChild(childModule);
            _declaredChildren.Add(childModule);
        }
    }

    /// <summary>
    ///     Rejects a build whose queued child modules were never declared through the registry.
    /// </summary>
    /// <param name="moduleBuilder">The populated module builder holding the queued child modules.</param>
    /// <exception cref="AuditConfigurationException">A queued child module was never declared.</exception>
    /// <remarks>
    ///     A child module queued by a store or anchor extension registers its own services when it builds, so
    ///     building the parent without declaring it leaves a composition that resolves nothing the child owns.
    ///     Compositions always declare children during registration; only a direct <see cref="Build" /> call
    ///     without a preceding <see cref="DeclareChildren" /> reaches this.
    /// </remarks>
    private void ThrowIfChildrenWereNeverDeclared(AuditingModuleBuilder moduleBuilder)
    {
        foreach (var childModule in moduleBuilder.CollectSubModules())
        {
            if (!IsAlreadyDeclared(childModule))
            {
                var childType = childModule.GetType();

                throw new AuditConfigurationException(
                    $"The auditing module queued the child module '{childType.FullName ?? childType.Name}' but it " +
                    "was never declared through the module registry, so its services are not registered. Register " +
                    "the auditing module through IModuleRegistry instead of building it directly.");
            }
        }
    }

    /// <summary>
    ///     Determines whether a child module was already declared through the module registry.
    /// </summary>
    /// <param name="childModule">The child module collected from the builder.</param>
    /// <returns><see langword="true" /> when the same instance was already declared; otherwise, <see langword="false" />.</returns>
    private bool IsAlreadyDeclared(IModule childModule)
    {
        foreach (var declared in _declaredChildren)
        {
            if (ReferenceEquals(declared, childModule))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Registers the chain engine: the store, mapper, segmenter, components, facade, and background work.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <param name="chain">The hash chain configuration.</param>
    /// <param name="documentType">The explicit or storage-default document type, or <see langword="null" /> when neither was configured.</param>
    private static void BuildChain(
        IModuleConfiguration configuration,
        AuditingOptions options,
        AuditHashChainOptions chain,
        Type? documentType)
    {
        var document = RequireDocument(documentType);
        var mapperType = RequireMapper(options, chain);
        var segmenterType = RequireSegmenter(options, chain);
        var storeType = RequireChainStoreType(options);
        var trailType = RequireTrailType(options);

        ThrowIfInvalidDocument(document, storeType);
        RunSchemeSelfTest(chain.SchemeName);

        var closedStoreInterface = typeof(IAuditChainStore<>).MakeGenericType(document);
        var closedMapperInterface = typeof(IAuditRecordMapper<>).MakeGenericType(document);
        var closedSegmenterInterface = typeof(IAuditSegmenter<>).MakeGenericType(document);
        var closedStoreType = CloseOverDocument(storeType, document, closedStoreInterface, "audit chain store");
        var closedTrailType = CloseOverDocument(trailType, document, typeof(IAuditTrail), "audit trail");

        ThrowIfNotImplementation(mapperType, closedMapperInterface, "record mapper registered through StoreAs");
        ThrowIfNotImplementation(segmenterType, closedSegmenterInterface, "segmenter registered through SegmentBy");

        var storeLifetime = StoreLifetime(options);
        var registry = configuration.DependencyRegistry;

        registry.Register(new DependencyDescriptor(typeof(AuditHashChainOptions), chain));
        registry.Register(new DependencyDescriptor(closedStoreInterface, closedStoreType, storeLifetime));
        registry.Register(new DependencyDescriptor(
            closedStoreType,
            provider => RequiredService(provider, closedStoreInterface),
            storeLifetime));
        registry.Register(new DependencyDescriptor(closedTrailType, closedTrailType, InstanceLifetime.Scoped));
        registry.Register(new DependencyDescriptor(closedMapperInterface, mapperType, InstanceLifetime.Singleton));
        registry.Register(new DependencyDescriptor(closedSegmenterInterface, segmenterType, InstanceLifetime.Singleton));

        // The shipped schemes resolve the canonicalizer for the stored document from the container. A storage
        // package supplies one for its own document as a replaceable default; a custom document brings its own.
        if (options.StoreDefaultCanonicalizerType is not null && document == options.StoreDefaultDocumentType)
        {
            var closedCanonicalizerInterface = typeof(IAuditCanonicalizer<>).MakeGenericType(document);

            ThrowIfNotImplementation(
                options.StoreDefaultCanonicalizerType,
                closedCanonicalizerInterface,
                "canonicalizer supplied by the audit store extension");

            registry.Register(DependencyDescriptor.ForDefaultPort(
                closedCanonicalizerInterface,
                options.StoreDefaultCanonicalizerType,
                InstanceLifetime.Singleton));
        }

        registry.Register(new DependencyDescriptor(
            typeof(AuditHashSchemeStartupCheck),
            provider => new AuditHashSchemeStartupCheck(provider, document, chain.SchemeName),
            InstanceLifetime.Singleton));
        configuration.RegisterStartupTask(typeof(AuditHashSchemeStartupCheck));

        RegisterNamedPieces(registry, chain);
        MoveStoreSourceFirst(chain, closedStoreType);
        RegisterEngine(registry, chain, document, closedStoreInterface, closedSegmenterInterface, closedTrailType);
        RegisterBackgroundWork(configuration);
    }

    /// <summary>
    ///     Registers auditing without the hash chain: the backend trail with the store and pieces it can close.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <param name="documentType">The storage-default document type, or <see langword="null" /> when the store supplies none.</param>
    /// <remarks>
    ///     Linking applies only with the chain on, so no engine component is registered here. The mapper and
    ///     segmenter register only when the store supplies them, because without the chain builder there is no
    ///     explicit call to name them.
    /// </remarks>
    private static void BuildTrailOnly(
        IModuleConfiguration configuration,
        AuditingOptions options,
        Type? documentType)
    {
        var trailType = RequireTrailType(options);
        var registry = configuration.DependencyRegistry;

        if (documentType is null)
        {
            if (trailType.IsGenericTypeDefinition)
            {
                throw new AuditConfigurationException(
                    $"The audit store records the open-generic trail '{trailType.FullName}' with no default document type, " +
                    "so the trail cannot be built. Reference a storage package that supplies document defaults, or enable " +
                    "the hash chain with StoreAs on the chain builder.");
            }

            ThrowIfNotImplementation(trailType, typeof(IAuditTrail), "audit trail recorded by the audit store extension");
            registry.Register(new DependencyDescriptor(trailType, trailType, InstanceLifetime.Scoped));

            if (options.ChainStoreType is { IsGenericTypeDefinition: false } closedStoreType)
            {
                registry.Register(new DependencyDescriptor(
                    closedStoreType,
                    closedStoreType,
                    StoreLifetime(options)));
            }

            RegisterCaptureCoordinator(registry, trailType);

            return;
        }

        ThrowIfInvalidDocument(documentType, options.ChainStoreType ?? trailType);

        var closedTrailType = CloseOverDocument(trailType, documentType, typeof(IAuditTrail), "audit trail");

        registry.Register(new DependencyDescriptor(closedTrailType, closedTrailType, InstanceLifetime.Scoped));

        if (options.ChainStoreType is not null)
        {
            var closedStoreInterface = typeof(IAuditChainStore<>).MakeGenericType(documentType);
            var closedStoreType = CloseOverDocument(
                options.ChainStoreType,
                documentType,
                closedStoreInterface,
                "audit chain store");
            var storeLifetime = StoreLifetime(options);

            registry.Register(new DependencyDescriptor(closedStoreInterface, closedStoreType, storeLifetime));
            registry.Register(new DependencyDescriptor(
                closedStoreType,
                provider => RequiredService(provider, closedStoreInterface),
                storeLifetime));
        }

        if (options.StoreDefaultMapperType is not null)
        {
            var closedMapperInterface = typeof(IAuditRecordMapper<>).MakeGenericType(documentType);

            ThrowIfNotImplementation(
                options.StoreDefaultMapperType,
                closedMapperInterface,
                "record mapper supplied by the audit store extension");

            registry.Register(new DependencyDescriptor(
                closedMapperInterface,
                options.StoreDefaultMapperType,
                InstanceLifetime.Singleton));
        }

        if (options.StoreDefaultSegmenterType is not null)
        {
            var closedSegmenterInterface = typeof(IAuditSegmenter<>).MakeGenericType(documentType);

            ThrowIfNotImplementation(
                options.StoreDefaultSegmenterType,
                closedSegmenterInterface,
                "segmenter supplied by the audit store extension");

            registry.Register(new DependencyDescriptor(
                closedSegmenterInterface,
                options.StoreDefaultSegmenterType,
                InstanceLifetime.Singleton));
        }

        RegisterCaptureCoordinator(registry, closedTrailType);
    }

    /// <summary>
    ///     Registers the capture coordinator, which writes records through the backend trail.
    /// </summary>
    /// <param name="registry">The dependency registry receiving the registration.</param>
    /// <param name="closedTrailType">The closed backend trail type the coordinator writes through.</param>
    private static void RegisterCaptureCoordinator(IDependencyRegistry registry, Type closedTrailType)
    {
        registry.Register(new DependencyDescriptor(
            typeof(IAuditCaptureCoordinator),
            provider => new AuditCaptureCoordinator(
                (IAuditTrail) RequiredService(provider, closedTrailType),
                (ITransactionalOutbox?) provider.GetService(typeof(ITransactionalOutbox))),
            InstanceLifetime.Scoped));
    }

    /// <summary>
    ///     Registers the capture entry trail as the <see cref="IAuditTrail" /> the mediation completion stage writes to.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    /// <param name="options">The auditing configuration carrying the effective capture mode.</param>
    /// <remarks>
    ///     The entry trail resolves the per-message capture mode and hands each record to the coordinator with it.
    ///     The storage package trail stays behind the coordinator as the backend that persists records.
    /// </remarks>
    private static void RegisterEntryTrail(IModuleConfiguration configuration, AuditingOptions options)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IAuditTrail),
            provider => new AuditCaptureTrail(
                (IAuditCaptureCoordinator) RequiredService(provider, typeof(IAuditCaptureCoordinator)),
                options.Capture),
            InstanceLifetime.Scoped));
    }

    /// <summary>
    ///     Registers the staged-segment holder and the commit listener that links staged records right after
    ///     the unit of work commits.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    /// <remarks>
    ///     Registered only with the chain on and the effective capture staging with the change for linking
    ///     after commit: the listener has nothing to link in any other mode, and the linker it calls exists
    ///     only with the chain on.
    /// </remarks>
    private static void RegisterAfterCommitLinking(IModuleConfiguration configuration)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(AuditStagedSegments),
            typeof(AuditStagedSegments),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
            typeof(IUnitOfWorkCommitListener),
            typeof(AuditLinkCommitListener),
            InstanceLifetime.Scoped));
    }

    /// <summary>
    ///     Registers the chain engine components and the <see cref="IAuditChain" /> facade over them.
    /// </summary>
    /// <param name="registry">The dependency registry receiving the registrations.</param>
    /// <param name="chain">The hash chain configuration the components read.</param>
    /// <param name="document">The effective stored document type.</param>
    /// <param name="closedStoreInterface">The closed chain store contract the components run against.</param>
    /// <param name="closedSegmenterInterface">The closed segmenter contract the linker opens segments with.</param>
    /// <param name="closedTrailType">The closed backend trail type the coordinator writes through.</param>
    private static void RegisterEngine(
        IDependencyRegistry registry,
        AuditHashChainOptions chain,
        Type document,
        Type closedStoreInterface,
        Type closedSegmenterInterface,
        Type closedTrailType)
    {
        var closedLinkerType = typeof(AuditLinker<>).MakeGenericType(document);
        var closedLifecycleType = typeof(CheckpointLifecycle<>).MakeGenericType(document);
        var closedVerifierType = typeof(AuditVerifier<>).MakeGenericType(document);
        var closedSegmentManagerType = typeof(AuditSegmentManager<>).MakeGenericType(document);
        var closedCheckpointStoreType = typeof(AuditCheckpointStore<>).MakeGenericType(document);
        var lifecycleLoggerType = typeof(ILogger<>).MakeGenericType(closedLifecycleType);
        var verifierLoggerType = typeof(ILogger<>).MakeGenericType(closedVerifierType);

        registry.Register(new DependencyDescriptor(
            typeof(IAuditHashSchemeRegistry),
            provider => new AuditHashSchemeRegistry(provider),
            InstanceLifetime.Singleton));

        registry.Register(new DependencyDescriptor(
            typeof(IAuditLinker),
            provider => CreateClosedComponent(
                closedLinkerType,
                RequiredService(provider, closedStoreInterface),
                RequiredService(provider, closedSegmenterInterface),
                RequiredService(provider, typeof(IAuditHashSchemeRegistry)),
                chain),
            InstanceLifetime.Singleton));

        registry.Register(new DependencyDescriptor(
            typeof(ICheckpointLifecycle),
            provider => CreateClosedComponent(
                closedLifecycleType,
                RequiredService(provider, closedStoreInterface),
                chain,
                provider,
                provider.GetService(typeof(TimeProvider)),
                provider.GetService(lifecycleLoggerType)),
            InstanceLifetime.Singleton));

        registry.Register(new DependencyDescriptor(
            typeof(IAuditVerifier),
            provider => CreateClosedComponent(
                closedVerifierType,
                RequiredService(provider, closedStoreInterface),
                RequiredService(provider, typeof(IAuditHashSchemeRegistry)),
                chain,
                provider,
                provider.GetService(typeof(TimeProvider)),
                provider.GetService(verifierLoggerType)),
            InstanceLifetime.Singleton));

        registry.Register(new DependencyDescriptor(
            typeof(IAuditSegmentManager),
            provider => CreateClosedComponent(
                closedSegmentManagerType,
                RequiredService(provider, closedStoreInterface),
                RequiredService(provider, typeof(ICheckpointLifecycle)),
                chain,
                TimeOrSystem(provider),
                provider),
            InstanceLifetime.Singleton));

        registry.Register(new DependencyDescriptor(
            typeof(IAuditCheckpointStore),
            provider => CreateClosedComponent(
                closedCheckpointStoreType,
                RequiredService(provider, closedStoreInterface)),
            InstanceLifetime.Singleton));

        RegisterCaptureCoordinator(registry, closedTrailType);

        registry.Register(new DependencyDescriptor(
            typeof(IAuditChain),
            provider => new AuditChain(
                (IAuditVerifier) RequiredService(provider, typeof(IAuditVerifier)),
                (ICheckpointLifecycle) RequiredService(provider, typeof(ICheckpointLifecycle)),
                (IAuditSegmentManager) RequiredService(provider, typeof(IAuditSegmentManager)),
                (IAuditCheckpointStore) RequiredService(provider, typeof(IAuditCheckpointStore))),
            InstanceLifetime.Singleton));
    }

    /// <summary>
    ///     Registers the linker sweep and checkpoint publish background work through manifest entries.
    /// </summary>
    /// <param name="configuration">The module configuration being built.</param>
    private static void RegisterBackgroundWork(IModuleConfiguration configuration)
    {
        var registry = configuration.DependencyRegistry;

        registry.Register(new DependencyDescriptor(
            typeof(AuditLinkSweepHostOptions),
            new AuditLinkSweepHostOptions()));
        registry.Register(new DependencyDescriptor(
            typeof(AuditLinkSweepBackgroundService),
            provider => new AuditLinkSweepBackgroundService(
                (IAuditLinker) RequiredService(provider, typeof(IAuditLinker)),
                (AuditLinkSweepHostOptions) RequiredService(provider, typeof(AuditLinkSweepHostOptions)),
                (TimeProvider?) provider.GetService(typeof(TimeProvider)),
                (ILogger<AuditLinkSweepBackgroundService>?) provider.GetService(typeof(ILogger<AuditLinkSweepBackgroundService>))),
            InstanceLifetime.Singleton));
        configuration.RegisterBackgroundService(typeof(AuditLinkSweepBackgroundService));

        registry.Register(new DependencyDescriptor(
            typeof(CheckpointPublishHostOptions),
            new CheckpointPublishHostOptions()));
        registry.Register(new DependencyDescriptor(
            typeof(CheckpointPublishBackgroundService),
            provider => new CheckpointPublishBackgroundService(
                provider,
                (TimeProvider?) provider.GetService(typeof(TimeProvider)),
                (ILogger<CheckpointPublishBackgroundService>?) provider.GetService(typeof(ILogger<CheckpointPublishBackgroundService>)),
                (CheckpointPublishHostOptions) RequiredService(provider, typeof(CheckpointPublishHostOptions))),
            InstanceLifetime.Singleton));
        configuration.RegisterBackgroundService(typeof(CheckpointPublishBackgroundService));
    }

    /// <summary>
    ///     Registers the checkpoint policy, signer, publisher, and source types the chain resolves from the container.
    /// </summary>
    /// <param name="registry">The dependency registry receiving the registrations.</param>
    /// <param name="chain">The hash chain configuration naming the pieces.</param>
    /// <remarks>
    ///     The lifecycle and the verifier resolve each piece by its concrete type when they run, so every named
    ///     type registers itself. A configured policy instance needs no registration: the lifecycle reads it from
    ///     these options, and falls back to a checkpoint every one hundred records when nothing is configured.
    /// </remarks>
    private static void RegisterNamedPieces(IDependencyRegistry registry, AuditHashChainOptions chain)
    {
        if (chain.CheckpointPolicyType is not null)
        {
            ThrowIfNotImplementation(
                chain.CheckpointPolicyType,
                typeof(ICheckpointPolicy),
                "checkpoint policy registered through CheckpointWhen");

            registry.Register(new DependencyDescriptor(
                chain.CheckpointPolicyType,
                chain.CheckpointPolicyType,
                InstanceLifetime.Singleton));
        }

        foreach (var signerType in chain.SignerTypes)
        {
            ThrowIfNotImplementation(signerType, typeof(ICheckpointSigner), "signer registered through SignCheckpointsWith");

            registry.Register(new DependencyDescriptor(signerType, signerType, InstanceLifetime.Singleton));
        }

        foreach (var publisherType in chain.PublisherTypes)
        {
            ThrowIfNotImplementation(
                publisherType,
                typeof(ICheckpointPublisher),
                "publisher registered through PublishCheckpointsTo");

            registry.Register(new DependencyDescriptor(publisherType, publisherType, InstanceLifetime.Singleton));
        }

        foreach (var sourceType in chain.SourceTypes)
        {
            ThrowIfNotImplementation(sourceType, typeof(ICheckpointSource), "source registered through VerifyAgainst");

            registry.Register(new DependencyDescriptor(sourceType, sourceType, InstanceLifetime.Singleton));
        }
    }

    /// <summary>
    ///     Moves the chain store to the front of the verification sources when it reads checkpoints back itself.
    /// </summary>
    /// <param name="chain">The hash chain configuration carrying the verification sources.</param>
    /// <param name="closedStoreType">The closed chain store type.</param>
    /// <remarks>
    ///     The verifier compares the chain against the first source as the stored checkpoint, so the store's own
    ///     source is always first and every registered source adds one comparison after it.
    /// </remarks>
    private static void MoveStoreSourceFirst(AuditHashChainOptions chain, Type closedStoreType)
    {
        if (!typeof(ICheckpointSource).IsAssignableFrom(closedStoreType))
        {
            return;
        }

        List<Type> sources = [closedStoreType];

        foreach (var sourceType in chain.SourceTypes)
        {
            if (sourceType != closedStoreType)
            {
                sources.Add(sourceType);
            }
        }

        chain.SourceTypes = sources;
    }

    /// <summary>
    ///     Proves at composition that the write scheme binds the chain position into every hash.
    /// </summary>
    /// <param name="schemeName">The scheme new records are written with.</param>
    /// <exception cref="AuditConfigurationException">The scheme name is not one the registry ships.</exception>
    /// <remarks>
    ///     The first shipped scheme passes its canonical bytes through unchanged because its canonical form
    ///     already includes the chain position, so the sample-based rule check does not apply to it and it is
    ///     skipped. Any other unknown name fails here, because the registry would fail the first resolution
    ///     with it anyway.
    /// </remarks>
    private static void RunSchemeSelfTest(string schemeName)
    {
        if (string.Equals(schemeName, AuditHashSchemeNames.Sha256V2, StringComparison.Ordinal))
        {
            AuditHashSchemeSelfTest.VerifyPositionBinding(schemeName, new FramedLinkRule(schemeName));
            return;
        }

        if (string.Equals(schemeName, AuditHashSchemeNames.Sha256V1, StringComparison.Ordinal))
        {
            return;
        }

        throw new AuditConfigurationException(
            $"The audit hash chain names the unknown hash scheme '{schemeName}'. The shipped schemes are " +
            $"'{AuditHashSchemeNames.Sha256V1}' and '{AuditHashSchemeNames.Sha256V2}'. " +
            "Name one of them through UseScheme.");
    }

    /// <summary>
    ///     Requires an audit store and returns what it can do.
    /// </summary>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <returns>What the registered audit store can do.</returns>
    /// <exception cref="AuditConfigurationException">No audit store is registered, or the registered store reports no capabilities.</exception>
    private static AuditStoreCapabilities RequireStoreCapabilities(AuditingOptions options)
    {
        if (options.TrailType is null && options.ChainStoreType is null)
        {
            throw new AuditConfigurationException(
                "Auditing is enabled but no audit store is registered, so audit records cannot be written. Call " +
                "UseMartenStore, UsePostgreSqlStore, UseEntityFrameworkCoreStore, or UseInMemoryStore on the auditing " +
                "module builder.");
        }

        return options.StoreCapabilities ?? throw new AuditConfigurationException(
            $"An audit store is registered (trail '{options.TrailType?.FullName}', chain store " +
            $"'{options.ChainStoreType?.FullName}') but reports no capabilities. A storage package must record its " +
            "AuditStoreCapabilities through its AuditingModuleBuilder extension.");
    }

    /// <summary>
    ///     Requires the stored document type the chain writes.
    /// </summary>
    /// <param name="documentType">The explicit or storage-default document type, or <see langword="null" /> when neither was configured.</param>
    /// <returns>The stored document type.</returns>
    /// <exception cref="AuditConfigurationException">Neither an explicit document type nor a storage default exists.</exception>
    private static Type RequireDocument(Type? documentType)
    {
        return documentType ?? throw new AuditConfigurationException(
            "The audit hash chain is on but names no stored document type. Call StoreAs on the hash chain builder, " +
            "or reference a storage package that supplies a default document type.");
    }

    /// <summary>
    ///     Requires the record mapper type the chain maps records with.
    /// </summary>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <param name="chain">The hash chain configuration.</param>
    /// <returns>The record mapper type.</returns>
    /// <exception cref="AuditConfigurationException">Neither an explicit mapper type nor a storage default exists.</exception>
    private static Type RequireMapper(AuditingOptions options, AuditHashChainOptions chain)
    {
        return chain.MapperType ?? options.StoreDefaultMapperType ?? throw new AuditConfigurationException(
            "The audit hash chain is on but names no record mapper. Call StoreAs on the hash chain builder, or " +
            "reference a storage package that supplies a default mapper.");
    }

    /// <summary>
    ///     Requires the segmenter type the chain assigns segments with.
    /// </summary>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <param name="chain">The hash chain configuration.</param>
    /// <returns>The segmenter type.</returns>
    /// <exception cref="AuditConfigurationException">Neither an explicit segmenter type nor a storage default exists.</exception>
    private static Type RequireSegmenter(AuditingOptions options, AuditHashChainOptions chain)
    {
        return chain.SegmenterType ?? options.StoreDefaultSegmenterType ?? throw new AuditConfigurationException(
            "The audit hash chain is on but names no segmenter. Call SegmentBy on the hash chain builder, or " +
            "reference a storage package that supplies a default segmenter.");
    }

    /// <summary>
    ///     Requires the chain store type the chain runs against.
    /// </summary>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <returns>The chain store type.</returns>
    /// <exception cref="AuditConfigurationException">No chain store is registered.</exception>
    private static Type RequireChainStoreType(AuditingOptions options)
    {
        return options.ChainStoreType ?? throw new AuditConfigurationException(
            "The audit hash chain is on but no chain store is registered. Call UseMartenStore, UsePostgreSqlStore, " +
            "UseEntityFrameworkCoreStore, or UseInMemoryStore on the auditing module builder.");
    }

    /// <summary>
    ///     Requires the backend trail type the capture coordinator writes through.
    /// </summary>
    /// <param name="options">The auditing configuration the builder recorded.</param>
    /// <returns>The backend trail type.</returns>
    /// <exception cref="AuditConfigurationException">No audit trail is registered.</exception>
    private static Type RequireTrailType(AuditingOptions options)
    {
        return options.TrailType ?? throw new AuditConfigurationException(
            "Auditing is enabled but the registered audit store records no trail, and the capture coordinator " +
            "writes through one. Reference a storage package that records a trail, such as the Marten, PostgreSQL, " +
            "or Entity Framework Core audit store.");
    }

    /// <summary>
    ///     Requires a stored document type the chain can close its generic pieces over.
    /// </summary>
    /// <param name="documentType">The stored document type.</param>
    /// <param name="storeType">The store type named in the error when the document type is unusable.</param>
    /// <exception cref="AuditConfigurationException">The document type is not a concrete chain document.</exception>
    private static void ThrowIfInvalidDocument(Type documentType, Type storeType)
    {
        if (!documentType.IsClass || documentType.IsAbstract || !typeof(IAuditChainDocument).IsAssignableFrom(documentType))
        {
            throw new AuditConfigurationException(
                $"The audit store '{storeType.FullName}' resolves the stored document type " +
                $"'{documentType.FullName}', which must be a concrete class implementing IAuditChainDocument.");
        }
    }

    /// <summary>
    ///     Closes an open-generic store piece over the document type and requires its contract.
    /// </summary>
    /// <param name="type">The open-generic or closed piece type the store recorded.</param>
    /// <param name="documentType">The stored document type open generics close over.</param>
    /// <param name="contract">The closed contract the piece must implement.</param>
    /// <param name="role">The piece role named in the error, such as the audit chain store.</param>
    /// <returns>The closed piece type.</returns>
    /// <exception cref="AuditConfigurationException">The piece closes over another document type or misses its contract.</exception>
    private static Type CloseOverDocument(Type type, Type documentType, Type contract, string role)
    {
        Type closed;

        if (type.IsGenericTypeDefinition)
        {
            closed = type.MakeGenericType(documentType);
        }
        else
        {
            if (type.IsGenericType && type.GetGenericArguments().Length == 1 && type.GetGenericArguments()[0] != documentType)
            {
                throw new AuditConfigurationException(
                    $"The {role} '{type.FullName}' closes over the document type " +
                    $"'{type.GetGenericArguments()[0].FullName}', but the auditing composition stores " +
                    $"'{documentType.FullName}'. Close the store over the configured document type.");
            }

            closed = type;
        }

        ThrowIfNotImplementation(closed, contract, role + " recorded by the audit store extension");

        return closed;
    }

    /// <summary>
    ///     Requires a piece type the container can build for its contract.
    /// </summary>
    /// <param name="type">The piece type.</param>
    /// <param name="contract">The contract the piece must implement.</param>
    /// <param name="role">The piece role named in the error, such as the segmenter.</param>
    /// <exception cref="AuditConfigurationException">The piece is abstract or misses its contract.</exception>
    private static void ThrowIfNotImplementation(Type type, Type contract, string role)
    {
        if (!contract.IsAssignableFrom(type))
        {
            throw new AuditConfigurationException(
                $"The {role} '{type.FullName}' does not implement '{contract.FullName}'.");
        }

        if (!type.IsClass || type.IsAbstract)
        {
            throw new AuditConfigurationException(
                $"The {role} '{type.FullName}' must be a concrete class the container can build.");
        }
    }

    /// <summary>
    ///     Describes the checkpoint policy the composition reports.
    /// </summary>
    /// <param name="chain">The hash chain configuration, or <see langword="null" /> when the chain is off.</param>
    /// <returns>The policy description, or <see langword="null" /> when the chain is off.</returns>
    private static string? DescribeCheckpointPolicy(AuditHashChainOptions? chain)
    {
        if (chain is null)
        {
            return null;
        }

        if (chain.CheckpointPolicy is not null)
        {
            var instanceType = chain.CheckpointPolicy.GetType();

            return "instance: " + (instanceType.FullName ?? instanceType.Name);
        }

        if (chain.CheckpointPolicyType is not null)
        {
            return "type: " + (chain.CheckpointPolicyType.FullName ?? chain.CheckpointPolicyType.Name);
        }

        return "default: every 100 records";
    }

    /// <summary>
    ///     Reads the store lifetime from what the store can do.
    /// </summary>
    /// <param name="options">The auditing configuration carrying the store capabilities.</param>
    /// <returns>Scoped when the store joins the business transaction; otherwise, Singleton.</returns>
    /// <remarks>
    ///     A store that joins the business transaction captures the scoped session, so it is scoped with it. A
    ///     store that cannot join runs on its own sessions, so one shared instance serves every caller.
    /// </remarks>
    private static InstanceLifetime StoreLifetime(AuditingOptions options)
    {
        return options.StoreCapabilities!.JoinsBusinessTransaction
            ? InstanceLifetime.Scoped
            : InstanceLifetime.Singleton;
    }

    /// <summary>
    ///     Creates a closed engine component by invoking its constructor with explicit arguments.
    /// </summary>
    /// <param name="closedType">The closed component type. Must declare a constructor matching the arguments.</param>
    /// <param name="arguments">The constructor arguments, resolved from the provider by the caller.</param>
    /// <returns>The created component.</returns>
    /// <exception cref="AuditConfigurationException">The component declares no constructor matching the arguments.</exception>
    /// <remarks>
    ///     The engine implementations are internal generic classes closed over the document type at composition,
    ///     so the module constructs them directly instead of asking the container to. Each call site passes the
    ///     arguments the matching constructor declares.
    /// </remarks>
    private static object CreateClosedComponent(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
        Type closedType,
        params object?[] arguments)
    {
        try
        {
            return Activator.CreateInstance(
                closedType,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                args: arguments,
                culture: null)!;
        }
        catch (MissingMethodException exception)
        {
            throw new AuditConfigurationException(
                $"The audit engine component '{closedType.FullName}' declares no constructor matching the arguments " +
                "the auditing module passes. Register the component the module expects.",
                exception);
        }
    }

    /// <summary>
    ///     Resolves a required service or fails with the missing service type.
    /// </summary>
    /// <param name="provider">The provider the service is resolved from.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The resolved service.</returns>
    /// <exception cref="LiteBusDependencyResolutionException">The service is not registered.</exception>
    private static object RequiredService(IServiceProvider provider, Type serviceType)
    {
        return provider.GetService(serviceType) ?? throw new LiteBusDependencyResolutionException(serviceType);
    }

    /// <summary>
    ///     Resolves the host clock or falls back to the system clock.
    /// </summary>
    /// <param name="provider">The provider the clock is resolved from.</param>
    /// <returns>The registered clock, or <see cref="TimeProvider.System" /> when none is registered.</returns>
    private static TimeProvider TimeOrSystem(IServiceProvider provider)
    {
        return (TimeProvider?) provider.GetService(typeof(TimeProvider)) ?? TimeProvider.System;
    }
}
