using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Abstractions.Authorization;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Authorization;
using LiteBus.Messaging.Registry;
using LiteBus.Messaging.Tenancy;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging.Composition;

/// <summary>
///     Read-only view of everything one host composed, for composition checks and export.
/// </summary>
/// <remarks>
///     <para>
///         A snapshot, materialized once when it is created. The registries no longer change after every module has
///         built, so the snapshot stays accurate for the life of the composition, and sharing one instance across
///         every composition check costs one traversal of the registry.
///     </para>
///     <para>
///         Two paths create it. <see cref="CreateForValidation" /> runs during composition validation, before any
///         container exists, and reports ports as LiteBus composed them. <see cref="CreateResolved" /> runs on first
///         resolve from the container and reports the effective port implementations, which include replacements the
///         application registered directly with its own container after LiteBus composed. Every other member is built
///         the same way on both paths; only <see cref="Ports" /> can differ, and only for ports the application
///         replaced outside LiteBus composition.
///     </para>
///     <para>
///         Every list has a stated order, so <see cref="ToJson" /> is byte-identical across runs for the same
///         composition: declarations sort by declaration type name, contracts by contract name then version, ports by
///         port type name, sections and policy names ordinally.
///     </para>
/// </remarks>
internal sealed class CompositionModel : ICompositionModel
{
    /// <summary>
    ///     The snapshot schema version written to the <c>schemaVersion</c> field of <see cref="ToJson" />.
    /// </summary>
    /// <remarks>
    ///     Incremented when the exported document gains, loses, or renames a field, so CI diffs and retained
    ///     compliance records can tell which shape they are reading.
    /// </remarks>
    internal const int SchemaVersion = 1;

    /// <summary>
    ///     The JSON writer settings shared by every <see cref="ToJson" /> call.
    /// </summary>
    /// <remarks>
    ///     Indented, because the snapshot is a compliance artifact a person reads as well as a file CI diffs.
    ///     The document is built from ordinally sorted dictionaries and pre-sorted lists rather than from an object
    ///     graph, so key order is ordinal at every level instead of reflection order.
    /// </remarks>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    ///     The messaging port types and the LiteBus default implementation of each, or
    ///     <see langword="null" /> when the port ships no default.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A port is an interface the application implements and registers to replace built-in behavior, such as
    ///         the tenant metadata provider or a permission lookup. Single-registration machinery such as the message
    ///         registry, the mediator, and the dispatch scope factory is not listed: the application never implements
    ///         those. Multi-registration collections such as authorization conditions and fault refusal classifiers
    ///         are not listed either, because one port entry names one implementation. The clock is ambient
    ///         infrastructure rather than a replaceable behavior and is not listed.
    ///     </para>
    ///     <para>
    ///         Features outside messaging join without a table change by registering their defaults through
    ///         <see cref="DependencyDescriptor.ForDefaultPort(Type, Type, InstanceLifetime)" />: any marked descriptor becomes a port entry.
    ///         A marked factory resolves to the table default for a known port, because the mark proves LiteBus
    ///         registered it and the table names what that factory produces.
    ///     </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<Type, Type?> KnownDefaultImplementations =
        new Dictionary<Type, Type?>
        {
            [typeof(IMessageSerializer)] = typeof(SystemTextJsonMessageSerializer),
            [typeof(IAuditOutcomeMapper)] = typeof(DefaultAuditOutcomeMapper),
            [typeof(IAuditRecordWriter)] = typeof(AuditRecordWriter),
            [typeof(IAuditTrail)] = null,
            [typeof(IAuditActorResolver)] = null,
            [typeof(IPermissionLookup)] = null,
            [typeof(ISignInInstantSource)] = null,
            [typeof(ITenantMetadataProvider)] = typeof(DefaultTenantMetadataProvider),
            [typeof(ITenantIdentifierValidator)] = typeof(DefaultTenantIdentifierValidator),
        };

    /// <summary>
    ///     The effective declaration records by registered message type.
    /// </summary>
    private readonly IReadOnlyDictionary<Type, IReadOnlyList<MessageDeclarationRecord>> _declarations;

    /// <summary>
    ///     The contract registrations the composition holds, in ordinal contract name, then version, order.
    /// </summary>
    private readonly IReadOnlyList<ContractRegistration> _contracts;

    /// <summary>
    ///     The policy names messages declare, in ordinal order.
    /// </summary>
    private readonly IReadOnlyCollection<string> _declaredPolicies;

    /// <summary>
    ///     Every registered message and what it declares.
    /// </summary>
    private readonly IMessageCatalog _messages;

    /// <summary>
    ///     The policy names the composition registered, in ordinal order.
    /// </summary>
    private readonly IReadOnlyCollection<string> _registeredPolicies;

    /// <summary>
    ///     The sections features contributed to the composition, in ordinal section name order.
    /// </summary>
    private readonly IReadOnlyList<ICompositionSectionContributor> _sections;

    /// <summary>
    ///     The resolved implementation of every port, in ordinal port type name order.
    /// </summary>
    private readonly IReadOnlyList<CompositionPortEntry> _ports;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CompositionModel" /> class.
    /// </summary>
    /// <param name="messages">Every registered message and what it declares.</param>
    /// <param name="declarations">The effective declaration records by registered message type.</param>
    /// <param name="declaredPolicies">The policy names messages declare, in ordinal order.</param>
    /// <param name="registeredPolicies">The policy names the composition registered, in ordinal order.</param>
    /// <param name="contracts">The contract registrations the composition holds.</param>
    /// <param name="ports">The resolved implementation of every port.</param>
    /// <param name="sections">The sections features contributed to the composition.</param>
    private CompositionModel(
        IMessageCatalog messages,
        IReadOnlyDictionary<Type, IReadOnlyList<MessageDeclarationRecord>> declarations,
        IReadOnlyCollection<string> declaredPolicies,
        IReadOnlyCollection<string> registeredPolicies,
        IReadOnlyList<ContractRegistration> contracts,
        IReadOnlyList<CompositionPortEntry> ports,
        IReadOnlyList<ICompositionSectionContributor> sections)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(declaredPolicies);
        ArgumentNullException.ThrowIfNull(registeredPolicies);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(sections);

        _messages = messages;
        _declarations = declarations;
        _declaredPolicies = declaredPolicies;
        _registeredPolicies = registeredPolicies;
        _contracts = contracts;
        _ports = ports;
        _sections = sections;
    }

    /// <inheritdoc />
    public IMessageCatalog Messages => _messages;

    /// <inheritdoc />
    public IReadOnlyCollection<string> DeclaredPolicyNames => _declaredPolicies;

    /// <inheritdoc />
    public IReadOnlyCollection<string> RegisteredPolicyNames => _registeredPolicies;

    /// <inheritdoc />
    public IReadOnlyList<ContractRegistration> Contracts => _contracts;

    /// <inheritdoc />
    public IReadOnlyList<CompositionPortEntry> Ports => _ports;

    /// <inheritdoc />
    public IReadOnlyList<ICompositionSectionContributor> Sections => _sections;

    /// <summary>
    ///     Builds the composition model handed to <c>ValidateComposition</c> checks.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="contracts">The contract registry holding every registered message contract.</param>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <returns>The model the checks assert over.</returns>
    /// <remarks>
    ///     Runs during composition validation, before any container exists, so ports are reported as LiteBus
    ///     composed them: a marked descriptor is the framework default still in place, and an explicit descriptor
    ///     for a known port is an application replacement. Replacements the application registers directly with
    ///     its own container afterwards are not visible yet; the resolved model reports those.
    /// </remarks>
    internal static CompositionModel CreateForValidation(
        IMessageReader reader,
        MessageContractRegistry contracts,
        IReadOnlyCollection<DependencyDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(descriptors);

        return CreateShared(reader, contracts, descriptors, PortsForValidation(descriptors));
    }

    /// <summary>
    ///     Builds the composition model resolved from the container.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="contracts">The contract registry holding every registered message contract.</param>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <param name="serviceProvider">The provider used to resolve the effective port implementations.</param>
    /// <returns>The model hosts log and tests assert over.</returns>
    /// <remarks>
    ///     <para>
    ///         Ports resolve through a dispatch scope, the way diagnostic probes resolve them, so a scoped
    ///         implementation resolves safely under a container that validates scopes. What the provider answers
    ///         is what the entry reports: a container replacement of a port wins over the LiteBus descriptor, and
    ///         a port nothing answers is absent rather than reported from the descriptor, because no implementation
    ///         will answer it at mediation either.
    ///     </para>
    ///     <para>
    ///         A port whose implementation cannot be constructed is reported from the dependency descriptors
    ///         instead of failing the whole report, exactly as the validation path reports it. Reporting the
    ///         composition must never depend on constructing a service, because a factory may throw for a
    ///         composition that is otherwise valid, such as the audit writer factory when no trail is
    ///         registered.
    ///     </para>
    /// </remarks>
    internal static CompositionModel CreateResolved(
        IMessageReader reader,
        MessageContractRegistry contracts,
        IReadOnlyCollection<DependencyDescriptor> descriptors,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        return CreateShared(reader, contracts, descriptors, PortsForResolved(descriptors, serviceProvider));
    }

    /// <summary>
    ///     Builds the model around one port list.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="contracts">The contract registry holding every registered message contract.</param>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <param name="ports">The port entries the construction path resolved.</param>
    /// <returns>The model.</returns>
    /// <remarks>
    ///     Everything but ports is built the same way on both construction paths, so the two models for one
    ///     composition agree on messages, declarations, policies, contracts, and sections.
    /// </remarks>
    private static CompositionModel CreateShared(
        IMessageReader reader,
        MessageContractRegistry contracts,
        IReadOnlyCollection<DependencyDescriptor> descriptors,
        IReadOnlyList<CompositionPortEntry> ports)
    {
        var messages = new MessageCatalog(reader);

        return new CompositionModel(
            messages,
            BuildDeclarations(reader),
            DeclaredPolicyNamesFor(messages),
            RegisteredPolicyNamesFor(descriptors),
            ContractsFor(contracts),
            ports,
            SectionsFor(descriptors));
    }

    /// <inheritdoc />
    public IReadOnlyList<MessageDeclarationRecord> DeclarationsFor(Type messageType)
    {
        ArgumentNullException.ThrowIfNull(messageType);

        return _declarations.TryGetValue(messageType, out var records) ? records : [];
    }

    /// <inheritdoc />
    public string ToJson()
    {
        SortedDictionary<string, object?> document = new(StringComparer.Ordinal)
        {
            ["schemaVersion"] = SchemaVersion,
            ["messages"] = MessagesForJson(),
            ["declaredPolicies"] = _declaredPolicies.ToList(),
            ["registeredPolicies"] = _registeredPolicies.ToList(),
            ["contracts"] = ContractsForJson(),
            ["ports"] = PortsForJson(),
            ["sections"] = SectionsForJson(),
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <summary>
    ///     Builds the effective declaration records for every registered message.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <returns>The records by message type, each list in ordinal declaration type name order.</returns>
    /// <remarks>
    ///     Descriptors whose metadata is not <see cref="MessageMetadata" /> report no records. Only the messaging
    ///     registry tracks where each value came from; a custom reader implementation carries values without
    ///     provenance, and inventing a source for them would misreport the composition.
    /// </remarks>
    private static Dictionary<Type, IReadOnlyList<MessageDeclarationRecord>> BuildDeclarations(
        IMessageReader reader)
    {
        Dictionary<Type, IReadOnlyList<MessageDeclarationRecord>> declarations = [];

        foreach (var descriptor in reader)
        {
            if (descriptor.Metadata is not MessageMetadata metadata)
            {
                continue;
            }

            List<MessageDeclarationRecord> records = [];

            foreach (var provenance in metadata.Provenance)
            {
                if (provenance.KeyType == typeof(DeclarationExemptions)
                    && provenance.Value is DeclarationExemptions exemptions)
                {
                    foreach (var exemption in exemptions.Values)
                    {
                        records.Add(ExpandExemption(provenance, exemption, descriptor.MessageType));
                    }

                    continue;
                }

                records.Add(new MessageDeclarationRecord(
                    provenance.KeyType,
                    provenance.Value,
                    provenance.Source,
                    provenance.DeclaredFor,
                    provenance.SourceType,
                    Rationale: null));
            }

            declarations[descriptor.MessageType] = records
                .OrderBy(record => record.DeclarationType.Name, StringComparer.Ordinal)
                .ThenBy(record => TypeName(record.DeclarationType), StringComparer.Ordinal)
                .ToList();
        }

        return declarations;
    }

    /// <summary>
    ///     Turns one exemption held by an effective exemption set into its own record.
    /// </summary>
    /// <param name="provenance">The provenance of the exemption set that holds the exemption.</param>
    /// <param name="exemption">The exemption to report.</param>
    /// <param name="messageType">The registered message type the set was resolved for.</param>
    /// <returns>The exemption record.</returns>
    /// <remarks>
    ///     The set aggregates many exemptions into one metadata value, so the record recovers what the set
    ///     collapsed: the exempted value type, the rationale, and, for an attribute set, the attribute type that
    ///     recorded this exemption rather than another one the message carries.
    /// </remarks>
    private static MessageDeclarationRecord ExpandExemption(
        MessageDeclarationProvenance provenance,
        DeclarationExemption exemption,
        Type messageType)
    {
        var sourceType = provenance.Source == DeclarationSourceKind.Attribute
            ? FindExemptionAttributeType(messageType, exemption)
            : provenance.SourceType;

        return new MessageDeclarationRecord(
            exemption.DeclarationType,
            exemption,
            DeclarationSourceKind.Exemption,
            provenance.DeclaredFor,
            sourceType,
            exemption.Rationale);
    }

    /// <summary>
    ///     Finds the attribute type that recorded one exemption on a message type.
    /// </summary>
    /// <param name="messageType">The registered message type carrying the exemption.</param>
    /// <param name="exemption">The exemption to attribute.</param>
    /// <returns>The matching attribute type, or <see langword="null" /> when none matches.</returns>
    /// <remarks>
    ///     Exemptions compare by value, so two attributes can record equal ones; the ordinally first attribute
    ///     type wins, which keeps the choice deterministic across runs.
    /// </remarks>
    private static Type? FindExemptionAttributeType(Type messageType, DeclarationExemption exemption)
    {
        Type? match = null;

        foreach (var attribute in messageType.GetCustomAttributes(inherit: true))
        {
            if (attribute is not IMessageDeclarationExemptionSource source
                || !source.CreateExemption().Equals(exemption))
            {
                continue;
            }

            var candidate = attribute.GetType();

            if (match is null
                || string.CompareOrdinal(TypeName(candidate), TypeName(match)) < 0)
            {
                match = candidate;
            }
        }

        return match;
    }

    /// <summary>
    ///     Collects the policy names messages declare.
    /// </summary>
    /// <param name="messages">Every registered message and what it declares.</param>
    /// <returns>The declared names in ordinal order.</returns>
    /// <remarks>
    ///     Only authorized declarations name a policy; an exempt message deliberately names none.
    /// </remarks>
    private static IReadOnlyCollection<string> DeclaredPolicyNamesFor(IMessageCatalog messages)
    {
        SortedSet<string> names = new(StringComparer.Ordinal);

        foreach (var entry in messages)
        {
            if (entry.Metadata.TryGet<AuthorizationDeclaration>(out var declaration)
                && declaration is AuthorizedDeclaration authorized)
            {
                names.Add(authorized.Policy);
            }
        }

        return [.. names];
    }

    /// <summary>
    ///     Reads the policy names the composition registered.
    /// </summary>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <returns>The registered names in ordinal order, or an empty list when no policy registry was composed.</returns>
    /// <remarks>
    ///     The names come from the composed <see cref="MessagePolicyRegistry" /> instance rather than from the
    ///     builder that collected them, so the model reports what the guard will read at mediation.
    /// </remarks>
    private static List<string> RegisteredPolicyNamesFor(
        IReadOnlyCollection<DependencyDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (!descriptor.IsCollectionRegistration
                && descriptor.DependencyType == typeof(MessagePolicyRegistry)
                && descriptor.Instance is MessagePolicyRegistry registry)
            {
                return registry.PolicyNames.OrderBy(name => name, StringComparer.Ordinal).ToList();
            }
        }

        return [];
    }

    /// <summary>
    ///     Lists the contract registrations the composition holds.
    /// </summary>
    /// <param name="contracts">The contract registry holding every registered message contract.</param>
    /// <returns>The registrations in ordinal contract name, then version, order.</returns>
    /// <remarks>
    ///     The owning assembly is the message type's assembly: a contract names the CLR type it persists, and
    ///     that type's assembly is what a composition check reads to tell which assembly owns each contract.
    /// </remarks>
    private static List<ContractRegistration> ContractsFor(MessageContractRegistry contracts)
    {
        return contracts.Snapshot()
            .Select(contract => new ContractRegistration(
                contract.Name,
                contract.Version,
                contract.MessageType,
                contract.MessageType.Assembly.GetName().Name ?? TypeName(contract.MessageType)))
            .OrderBy(registration => registration.Name, StringComparer.Ordinal)
            .ThenBy(registration => registration.Version)
            .ToList();
    }

    /// <summary>
    ///     Collects the sections features contributed to the composition.
    /// </summary>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <returns>The contributors in ordinal section name order.</returns>
    /// <exception cref="LiteBusConfigurationException">
    ///     A contributor was registered by type or through a factory rather than as an instance, names no
    ///     section, or names a section another contributor already contributes.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         A feature contributes by registering its contributor instance for
    ///         <see cref="ICompositionSectionContributor" />, as a collection item when several features each
    ///         contribute a section. The core reads the instances without referencing the feature package. Only
    ///         instances qualify: a type or factory needs a service provider to become data, and the model is
    ///         built without one.
    ///     </para>
    ///     <para>
    ///         One section has one contributor. Registering the same instance twice is a no-op; two different
    ///         contributors under one name fail composition naming both, because their data cannot be merged.
    ///         A feature spanning several assemblies collects its own registrations and contributes once.
    ///     </para>
    /// </remarks>
    private static List<ICompositionSectionContributor> SectionsFor(
        IReadOnlyCollection<DependencyDescriptor> descriptors)
    {
        List<ICompositionSectionContributor> sections = [];

        foreach (var descriptor in descriptors)
        {
            if (descriptor.DependencyType != typeof(ICompositionSectionContributor))
            {
                continue;
            }

            if (descriptor.Instance is not ICompositionSectionContributor contributor)
            {
                throw new LiteBusConfigurationException(
                    $"A composition section contributor must be registered as an instance, because the composition "
                    + $"model reads its section data without a service provider. {DescribeContributor(descriptor)} "
                    + "was registered instead; register the contributor instance.");
            }

            if (string.IsNullOrWhiteSpace(contributor.SectionName))
            {
                throw new LiteBusConfigurationException(
                    $"The composition section contributor '{TypeName(contributor.GetType())}' names no section. "
                    + "Name the section the contribution is exposed and serialized under.");
            }

            if (sections.Any(existing => ReferenceEquals(existing, contributor)))
            {
                continue;
            }

            var clash = sections.FirstOrDefault(existing => string.Equals(
                existing.SectionName,
                contributor.SectionName,
                StringComparison.Ordinal));

            if (clash is not null)
            {
                throw new LiteBusConfigurationException(
                    $"Both '{TypeName(clash.GetType())}' and '{TypeName(contributor.GetType())}' contribute the "
                    + $"composition section '{contributor.SectionName}'. Contribute one section per name; a feature "
                    + "spanning several assemblies collects its own registrations and contributes once.");
            }

            sections.Add(contributor);
        }

        return sections.OrderBy(section => section.SectionName, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    ///     Describes a misregistered section contributor for a composition error.
    /// </summary>
    /// <param name="descriptor">The descriptor registered for the contributor service type.</param>
    /// <returns>What was registered instead of a contributor instance.</returns>
    private static string DescribeContributor(DependencyDescriptor descriptor)
    {
        if (descriptor.ImplementationType is not null)
        {
            return $"The type '{TypeName(descriptor.ImplementationType)}'";
        }

        return "A factory registration";
    }

    /// <summary>
    ///     Reports ports as LiteBus composed them, from the dependency descriptors.
    /// </summary>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <returns>The port entries in ordinal port type name order.</returns>
    /// <remarks>
    ///     A marked descriptor is the framework default still in place. An explicit descriptor for a known port
    ///     is an application replacement. A factory the table cannot attribute to a known default product is
    ///     absent: reporting the port type as its own implementation would invent an answer, and the resolved
    ///     model reports what the factory actually produces.
    /// </remarks>
    private static List<CompositionPortEntry> PortsForValidation(
        IReadOnlyCollection<DependencyDescriptor> descriptors)
    {
        List<CompositionPortEntry> ports = [];

        foreach (var descriptor in descriptors)
        {
            if (descriptor.IsCollectionRegistration)
            {
                continue;
            }

            var known = KnownDefaultImplementations.TryGetValue(
                descriptor.DependencyType,
                out var knownDefault);

            if (!known && !descriptor.IsLiteBusDefault)
            {
                continue;
            }

            var implementation = descriptor.ImplementationType ?? descriptor.Instance?.GetType();

            if (implementation is null)
            {
                if (!descriptor.IsLiteBusDefault || !known || knownDefault is null)
                {
                    continue;
                }

                implementation = knownDefault;
            }

            ports.Add(new CompositionPortEntry(
                descriptor.DependencyType,
                implementation,
                descriptor.IsLiteBusDefault));
        }

        return OrderPorts(ports);
    }

    /// <summary>
    ///     Reports the effective port implementations, reading the container when construction succeeds.
    /// </summary>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <param name="serviceProvider">The provider used to resolve the effective port implementations.</param>
    /// <returns>The port entries in ordinal port type name order.</returns>
    /// <remarks>
    ///     <para>
    ///         What the provider answers is what the entry reports, so a replacement the application registered
    ///         directly with its own container is recorded as replaced. A port nothing answers is absent: no
    ///         implementation will answer it at mediation either. An entry is the framework default when the
    ///         effective implementation is the default the table or the marked descriptor names.
    ///     </para>
    ///     <para>
    ///         A port whose implementation cannot be constructed is reported from the dependency descriptors
    ///         instead of failing the whole report, exactly as the validation path reports it. Reporting the
    ///         composition must never depend on constructing a service, because a factory may throw for a
    ///         composition that is otherwise valid, such as the audit writer factory when no trail is
    ///         registered. The catch stays broad because each container surfaces a construction failure
    ///         differently: the built-in container propagates the factory exception as thrown, while other
    ///         containers wrap it, so no single exception type covers every host.
    ///     </para>
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Containers surface a factory failure as different exception types; a port that cannot be constructed is reported from its descriptors, as the remarks explain.")]
    private static List<CompositionPortEntry> PortsForResolved(
        IReadOnlyCollection<DependencyDescriptor> descriptors,
        IServiceProvider serviceProvider)
    {
        var portTypes = KnownDefaultImplementations.Keys
            .Concat(descriptors
                .Where(descriptor => !descriptor.IsCollectionRegistration && descriptor.IsLiteBusDefault)
                .Select(descriptor => descriptor.DependencyType))
            .Distinct()
            .OrderBy(TypeName, StringComparer.Ordinal)
            .ToList();

        using var scope = serviceProvider.GetService(typeof(IMessageDispatchScopeFactory))
            is IMessageDispatchScopeFactory factory
            ? factory.CreateScope()
            : null;

        var provider = scope?.ServiceProvider ?? serviceProvider;

        List<CompositionPortEntry> ports = [];

        foreach (var portType in portTypes)
        {
            Type? effective;
            var constructionFailed = false;

            try
            {
                effective = provider.GetService(portType)?.GetType();
            }
            catch (Exception)
            {
                effective = null;
                constructionFailed = true;
            }

            if (effective is not null)
            {
                ports.Add(new CompositionPortEntry(portType, effective, IsDefaultImplementation(descriptors, portType, effective)));
                continue;
            }

            if (!constructionFailed)
            {
                continue;
            }

            if (TryDescribeFromDescriptors(descriptors, portType, out var fallback))
            {
                ports.Add(fallback);
            }
        }

        return OrderPorts(ports);
    }

    /// <summary>
    ///     Describes one port from the dependency descriptors, the way the validation path reports it.
    /// </summary>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <param name="portType">The port interface to describe.</param>
    /// <param name="entry">The port entry, or <see langword="null" /> when the descriptors say nothing about the port.</param>
    /// <returns><see langword="true" /> when the descriptors describe the port.</returns>
    /// <remarks>
    ///     A marked factory resolves to the table default for a known port, because the mark proves LiteBus
    ///     registered it and the table names what that factory produces. Any other factory is absent:
    ///     reporting the port type as its own implementation would invent an answer.
    /// </remarks>
    private static bool TryDescribeFromDescriptors(
        IReadOnlyCollection<DependencyDescriptor> descriptors,
        Type portType,
        [NotNullWhen(true)] out CompositionPortEntry? entry)
    {
        entry = null;

        var known = KnownDefaultImplementations.TryGetValue(portType, out var knownDefault);

        foreach (var descriptor in descriptors)
        {
            if (descriptor.IsCollectionRegistration || descriptor.DependencyType != portType)
            {
                continue;
            }

            if (!known && !descriptor.IsLiteBusDefault)
            {
                continue;
            }

            var implementation = descriptor.ImplementationType ?? descriptor.Instance?.GetType();

            if (implementation is null)
            {
                if (!descriptor.IsLiteBusDefault || !known || knownDefault is null)
                {
                    continue;
                }

                implementation = knownDefault;
            }

            entry = new CompositionPortEntry(descriptor.DependencyType, implementation, descriptor.IsLiteBusDefault);
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Determines whether an effective port implementation is the framework default.
    /// </summary>
    /// <param name="descriptors">The dependency descriptors every module registered.</param>
    /// <param name="portType">The port interface the composition resolved.</param>
    /// <param name="effective">The effective implementation type the provider answered.</param>
    /// <returns>
    ///     <see langword="true" /> when the effective implementation is the default the known-port table or
    ///     the marked descriptor names.
    /// </returns>
    private static bool IsDefaultImplementation(
        IReadOnlyCollection<DependencyDescriptor> descriptors,
        Type portType,
        Type effective)
    {
        if (KnownDefaultImplementations.TryGetValue(portType, out var knownDefault)
            && knownDefault is not null)
        {
            return effective == knownDefault;
        }

        foreach (var descriptor in descriptors)
        {
            if (!descriptor.IsCollectionRegistration
                && descriptor.DependencyType == portType
                && descriptor.IsLiteBusDefault)
            {
                var marked = descriptor.ImplementationType ?? descriptor.Instance?.GetType();

                return marked is not null && effective == marked;
            }
        }

        return false;
    }

    /// <summary>
    ///     Orders port entries by port type name.
    /// </summary>
    /// <param name="ports">The entries to order.</param>
    /// <returns>The entries in ordinal port type name order.</returns>
    private static List<CompositionPortEntry> OrderPorts(List<CompositionPortEntry> ports)
    {
        return ports
            .OrderBy(entry => entry.PortType.Name, StringComparer.Ordinal)
            .ThenBy(entry => TypeName(entry.PortType), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Renders the messages and their declaration source tables for the snapshot document.
    /// </summary>
    /// <returns>One object per registered message, in ordinal message type name order.</returns>
    private List<object?> MessagesForJson()
    {
        return _declarations
            .OrderBy(pair => TypeName(pair.Key), StringComparer.Ordinal)
            .Select(pair => (object?) new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["message"] = TypeName(pair.Key),
                ["declarations"] = pair.Value
                    .Select(record => (object?) new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["declarationType"] = TypeName(record.DeclarationType),
                        ["value"] = ValueText(record.Value),
                        ["source"] = record.Source.ToString(),
                        ["declaredFor"] = TypeName(record.DeclaredFor),
                        ["sourceType"] = record.SourceType is null ? null : TypeName(record.SourceType),
                        ["rationale"] = record.Rationale,
                    })
                    .ToList(),
            })
            .ToList();
    }

    /// <summary>
    ///     Renders the contract registrations for the snapshot document.
    /// </summary>
    /// <returns>One object per contract, in ordinal contract name, then version, order.</returns>
    private List<object?> ContractsForJson()
    {
        return _contracts
            .Select(contract => (object?) new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = contract.Name,
                ["version"] = contract.Version,
                ["messageType"] = TypeName(contract.MessageType),
                ["assembly"] = contract.AssemblyName,
            })
            .ToList();
    }

    /// <summary>
    ///     Renders the port entries for the snapshot document.
    /// </summary>
    /// <returns>One object per port, in ordinal port type name order.</returns>
    private List<object?> PortsForJson()
    {
        return _ports
            .Select(port => (object?) new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["port"] = TypeName(port.PortType),
                ["implementation"] = TypeName(port.ImplementationType),
                ["isDefault"] = port.IsDefault,
            })
            .ToList();
    }

    /// <summary>
    ///     Renders the contributed sections for the snapshot document.
    /// </summary>
    /// <returns>The section data by section name, in ordinal name order.</returns>
    /// <remarks>
    ///     A section whose contributor answers <see langword="null" /> is serialized as null rather than
    ///     omitted, so the section set stays stable across compositions of the same modules.
    /// </remarks>
    private SortedDictionary<string, object?> SectionsForJson()
    {
        SortedDictionary<string, object?> sections = new(StringComparer.Ordinal);

        foreach (var contributor in _sections)
        {
            sections[contributor.SectionName] = contributor.GetData();
        }

        return sections;
    }

    /// <summary>
    ///     Names a type the way the snapshot identifies it.
    /// </summary>
    /// <param name="type">The type to name.</param>
    /// <returns>The full type name, or the type name when no full name exists.</returns>
    /// <remarks>
    ///     Full names stay stable across assembly versions, unlike assembly-qualified names, and stay precise
    ///     across namespaces, unlike bare names.
    /// </remarks>
    private static string TypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }

    /// <summary>
    ///     Renders a declaration value the way the snapshot records it.
    /// </summary>
    /// <param name="value">The effective declaration value.</param>
    /// <returns>The value rendered with the invariant culture.</returns>
    /// <remarks>
    ///     Values are application objects of arbitrary shape, some holding functions that no serializer
    ///     accepts, so the snapshot records their invariant string form rather than their structure. The
    ///     invariant culture keeps numeric values byte-identical across machines.
    /// </remarks>
    private static string ValueText(object value)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0}", value);
    }
}
