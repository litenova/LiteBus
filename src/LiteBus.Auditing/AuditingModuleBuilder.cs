using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Recording;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Auditing;

/// <summary>
///     Configures auditing: when audit records are captured, and whether they join a hash chain.
/// </summary>
/// <remarks>
///     Reached through <c>ModuleRegistryExtensions.AddAuditing</c>. A storage package adds its store with an extension
///     such as <c>UseMartenStore</c>; an anchor package adds its publisher and source with an extension on
///     <see cref="AuditHashChainBuilder" />. A store or anchor that needs settings registers its settings module as a
///     child through <see cref="RegisterChildModule" />, so the settings compose inside <c>AddAuditing</c>.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddAuditing(auditing => auditing
///     .Capture(AuditCapture.WithChange())
///     .UseHashChain(chain => chain
///         .CheckpointStaleAfter(TimeSpan.FromDays(1))));
/// ]]></code>
/// </example>
public sealed class AuditingModuleBuilder
{
    /// <summary>
    ///     The options this builder writes.
    /// </summary>
    private readonly AuditingOptions _options = new();

    /// <summary>
    ///     Child modules declared by storage and anchor packages composing through this builder.
    /// </summary>
    private readonly List<IModule> _childModules = [];

    /// <summary>
    ///     Gets the recorded options.
    /// </summary>
    internal AuditingOptions Options => _options;

    /// <summary>
    ///     Sets when and where audit records are written, relative to the business commit.
    /// </summary>
    /// <param name="capture">The capture mode. Defaults to <see cref="AuditCapture.WithChange" />.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     A message overrides this per message with an <see cref="AuditCaptureDeclaration" />. Composition picks
    ///     <see cref="AuditCapture.AfterCommitThroughOutbox" /> when the store cannot join the business
    ///     transaction, visibly in the composition model, and rejects an invalid capture and link pair with a
    ///     configuration exception.
    /// </remarks>
    public AuditingModuleBuilder Capture(AuditCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        _options.Capture = capture;

        return this;
    }

    /// <summary>
    ///     Chains captured records in a tamper-evident hash chain.
    /// </summary>
    /// <param name="configure">The chain configuration callback.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Calling this twice configures one chain instead of replacing it, so several application modules can add
    ///     to one composition.
    /// </remarks>
    public AuditingModuleBuilder UseHashChain(Action<AuditHashChainBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _options.Chain ??= new AuditHashChainOptions();
        configure(new AuditHashChainBuilder(_options.Chain, this));

        return this;
    }

    /// <summary>
    ///     Configures the background loop that publishes pending checkpoints and cuts the ones their policy reports
    ///     due.
    /// </summary>
    /// <param name="configure">The callback that sets the loop's settings.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     A product whose web host and worker host compose the same modules sets
    ///     <see cref="CheckpointPublishHostOptions.Enabled" /> to <see langword="false" /> in every host but one, so
    ///     the cutter and the retrier run once. The composition model's <c>auditing</c> section shows the setting.
    /// </remarks>
    public AuditingModuleBuilder ConfigureCheckpointPublishing(Action<CheckpointPublishHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options.CheckpointPublishing);
        return this;
    }

    /// <summary>
    ///     Configures the background loop that links staged records the after-commit listener did not link.
    /// </summary>
    /// <param name="configure">The callback that sets the loop's settings.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    public AuditingModuleBuilder ConfigureLinkSweep(Action<AuditLinkSweepHostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(_options.LinkSweep);
        return this;
    }

    /// <summary>
    ///     The action-code pattern <see cref="RequireActionFormat" /> applies when it is given none: a lowercase
    ///     kebab-case category, a dot, and a lowercase kebab-case action, such as <c>orders.place-order</c>.
    /// </summary>
    /// <remarks>
    ///     The convention every example in the LiteBus documentation uses. It is the default rather than a rule, because
    ///     an application that already has an action taxonomy should keep it.
    /// </remarks>
    public const string DefaultActionPattern = @"^[a-z0-9]+(?:-[a-z0-9]+)*\.[a-z0-9]+(?:-[a-z0-9]+)*$";

    /// <summary>
    ///     The record-production settings this builder writes: axes, trail, writer, actor resolver and outcome mapper.
    /// </summary>
    private readonly AuditRecordingOptions _recording = new();

    /// <summary>
    ///     Gets the recorded record-production settings.
    /// </summary>
    internal AuditRecordingOptions Recording => _recording;

    /// <summary>
    ///     Records every command mediation that declares an audit position.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The record is written at the completion stage, so refusals, failures and cancellations are recorded as well
    ///     as successes. A command batch buffers its records and writes them before its single commit.
    /// </remarks>
    public AuditingModuleBuilder ForCommands()
    {
        _recording.Commands = true;
        return this;
    }

    /// <summary>
    ///     Records every query mediation that declares an audit position.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AuditingModuleBuilder ForQueries()
    {
        _recording.Queries = true;
        return this;
    }

    /// <summary>
    ///     Records every event publication that declares an audit position.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AuditingModuleBuilder ForEvents()
    {
        _recording.Events = true;
        return this;
    }

    /// <summary>
    ///     Records commands, queries and events that declare an audit position.
    /// </summary>
    /// <returns>The current builder.</returns>
    public AuditingModuleBuilder ForAllAxes()
    {
        return ForCommands().ForQueries().ForEvents();
    }

    /// <summary>
    ///     Sends audit records to the application's own trail, constructed by the container, instead of an audit store.
    /// </summary>
    /// <typeparam name="TAuditTrail">The trail implementation.</typeparam>
    /// <param name="lifetime">The lifetime the trail is registered with. Defaults to scoped.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     An application trail and an audit store are alternatives: composition fails when both are configured,
    ///     because only one of them can receive the records. Capture modes and the hash chain need a store.
    /// </remarks>
    public AuditingModuleBuilder UseTrail<TAuditTrail>(InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditTrail : class, IAuditTrail
    {
        _recording.Trail = typeof(TAuditTrail);
        _recording.TrailLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Sends audit records to a pre-created application trail instead of an audit store.
    /// </summary>
    /// <param name="auditTrail">The trail instance, registered as a singleton.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="auditTrail" /> is <see langword="null" />.</exception>
    public AuditingModuleBuilder UseTrailInstance(IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(auditTrail);

        _recording.Trail = auditTrail;
        _recording.TrailLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Replaces the built-in record writer, which turns the end of a mediation into an <see cref="AuditRecord" />.
    /// </summary>
    /// <typeparam name="TAuditRecordWriter">The writer implementation.</typeparam>
    /// <param name="lifetime">The lifetime the writer is registered with. Defaults to scoped.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The seam for an application that wants its own record shape. A replacing writer takes over the whole of
    ///     record building: reading the declaration, enforcing a required reason, and reading what a handler supplied
    ///     through <see cref="IAuditScope" />.
    /// </remarks>
    public AuditingModuleBuilder UseRecordWriter<TAuditRecordWriter>(InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditRecordWriter : class, IAuditRecordWriter
    {
        _recording.RecordWriter = typeof(TAuditRecordWriter);
        _recording.RecordWriterLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Replaces the built-in record writer with a pre-created instance.
    /// </summary>
    /// <param name="auditRecordWriter">The writer instance, registered as a singleton.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="auditRecordWriter" /> is <see langword="null" />.</exception>
    public AuditingModuleBuilder UseRecordWriterInstance(IAuditRecordWriter auditRecordWriter)
    {
        ArgumentNullException.ThrowIfNull(auditRecordWriter);

        _recording.RecordWriter = auditRecordWriter;
        _recording.RecordWriterLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Registers the resolver that names the actor each record is attributed to.
    /// </summary>
    /// <typeparam name="TAuditActorResolver">The resolver implementation.</typeparam>
    /// <param name="lifetime">The lifetime the resolver is registered with. Defaults to scoped.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Without a resolver, a record is attributed from the execution identity's principal: the name identifier or
    ///     <c>sub</c> claim, with the name claim as the display name.
    /// </remarks>
    public AuditingModuleBuilder UseActorResolver<TAuditActorResolver>(InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditActorResolver : class, IAuditActorResolver
    {
        return UseActorResolver(typeof(TAuditActorResolver), lifetime);
    }

    /// <summary>
    ///     Registers the resolver that names the actor each record is attributed to, by type.
    /// </summary>
    /// <param name="resolverType">A concrete class implementing <see cref="IAuditActorResolver" />.</param>
    /// <param name="lifetime">The lifetime the resolver is registered with. Defaults to scoped.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolverType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="resolverType" /> is not a concrete class implementing <see cref="IAuditActorResolver" />.
    /// </exception>
    public AuditingModuleBuilder UseActorResolver(Type resolverType, InstanceLifetime lifetime = InstanceLifetime.Scoped)
    {
        ArgumentNullException.ThrowIfNull(resolverType);

        if (!typeof(IAuditActorResolver).IsAssignableFrom(resolverType) ||
            resolverType is { IsClass: false } or { IsAbstract: true })
        {
            throw new ArgumentException(
                $"'{resolverType.Name}' is not a concrete class implementing IAuditActorResolver, so it cannot resolve "
                + "the actor an audit record is attributed to.",
                nameof(resolverType));
        }

        _recording.ActorResolver = resolverType;
        _recording.ActorResolverLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers a pre-created resolver that names the actor each record is attributed to.
    /// </summary>
    /// <param name="auditActorResolver">The resolver instance, registered as a singleton.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="auditActorResolver" /> is <see langword="null" />.</exception>
    public AuditingModuleBuilder UseActorResolverInstance(IAuditActorResolver auditActorResolver)
    {
        ArgumentNullException.ThrowIfNull(auditActorResolver);

        _recording.ActorResolver = auditActorResolver;
        _recording.ActorResolverLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Replaces the built-in outcome mapper, which classifies how a mediation ended for the record.
    /// </summary>
    /// <param name="auditOutcomeMapper">The mapper instance.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="auditOutcomeMapper" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The mapper is read on the completion path of every audited mediation and must stay pure, so it is one
    ///     instance rather than resolved per scope. A replacement usually starts from
    ///     <see cref="DefaultAuditOutcomeMapper.MapByOutcome" /> and recognizes its own refusal exceptions.
    /// </remarks>
    public AuditingModuleBuilder UseOutcomeMapper(IAuditOutcomeMapper auditOutcomeMapper)
    {
        ArgumentNullException.ThrowIfNull(auditOutcomeMapper);

        _recording.OutcomeMapper = auditOutcomeMapper;
        return this;
    }

    /// <summary>
    ///     Replaces the built-in outcome mapper with a new instance of <typeparamref name="TAuditOutcomeMapper" />.
    /// </summary>
    /// <typeparam name="TAuditOutcomeMapper">The mapper implementation, with a parameterless constructor.</typeparam>
    /// <returns>The current builder.</returns>
    public AuditingModuleBuilder UseOutcomeMapper<TAuditOutcomeMapper>()
        where TAuditOutcomeMapper : IAuditOutcomeMapper, new()
    {
        _recording.OutcomeMapper = new TAuditOutcomeMapper();
        return this;
    }

    /// <summary>
    ///     Requires every audited message to declare an action code no other message declares.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Two messages sharing an action code make the trail unqueryable by use case, which is the one thing an action
    ///     code exists for. It is also the mistake copying a definition produces, and nothing else reports it.
    /// </remarks>
    public AuditingModuleBuilder RequireUniqueActions()
    {
        _recording.AddCheck(static model =>
        {
            var duplicates = model.Messages.Audited()
                .GroupBy(static entry => entry.GetAudit()!.Action, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group =>
                    $"  '{group.Key}' is declared by: {string.Join(", ", group.Select(static entry => entry.MessageType.Name))}")
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new AuditConfigurationException(
                    "Two or more audited messages declare the same audit action, so the trail cannot be queried by use case:"
                    + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
            }
        });

        return this;
    }

    /// <summary>
    ///     Requires every audit action code to match a pattern.
    /// </summary>
    /// <param name="pattern">
    ///     The regular expression each action code must match. Defaults to <see cref="DefaultActionPattern" />.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="pattern" /> is null, empty, or whitespace.</exception>
    public AuditingModuleBuilder RequireActionFormat(string pattern = DefaultActionPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var expression = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        _recording.AddCheck(model =>
        {
            var wrong = model.Messages.Audited()
                .Where(entry => !expression.IsMatch(entry.GetAudit()!.Action))
                .Select(static entry => $"  {entry.MessageType.Name} declares '{entry.GetAudit()!.Action}'")
                .OrderBy(static line => line, StringComparer.Ordinal)
                .ToList();

            if (wrong.Count > 0)
            {
                throw new AuditConfigurationException(
                    $"One or more audit actions do not match the required format '{pattern}':"
                    + Environment.NewLine + string.Join(Environment.NewLine, wrong));
            }
        });

        return this;
    }

    /// <summary>
    ///     Declares a child module owned by the auditing module.
    /// </summary>
    /// <param name="module">The child module to build alongside the auditing module.</param>
    /// <returns>The current <see cref="AuditingModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The seam storage and anchor packages compose through, so a settings module is registered inside
    ///     <c>AddAuditing</c> rather than as a second top-level module the caller has to remember to add.
    /// </remarks>
    public AuditingModuleBuilder RegisterChildModule(IModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        _childModules.Add(module);
        return this;
    }

    /// <summary>
    ///     Collects the child modules declared on this builder.
    /// </summary>
    /// <returns>The child modules, in declaration order.</returns>
    public IReadOnlyList<IModule> CollectSubModules()
    {
        return _childModules;
    }
}
