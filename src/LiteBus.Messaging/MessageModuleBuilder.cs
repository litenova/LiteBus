using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Audit;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Messaging;

/// <summary>
///     Configures handler and message type registration for the messaging module.
/// </summary>
public sealed class MessageModuleBuilder
{
    /// <summary>
    ///     Gets the shared message registry used to register handlers and message types.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     The default audit action format: a category, a dot, and a kebab-case action.
    /// </summary>
    /// <remarks>
    ///     The convention every example in the LiteBus documentation uses, such as <c>orders.place-order</c>. It is
    ///     the default rather than a rule, because an application that already has an action taxonomy should keep it.
    /// </remarks>
    public const string DefaultAuditActionPattern = @"^[a-z0-9]+(?:-[a-z0-9]+)*\.[a-z0-9]+(?:-[a-z0-9]+)*$";

    /// <summary>
    ///     The application composition checks to run once every module has been built.
    /// </summary>
    private readonly List<Action<IMessageCatalog>> _compositionChecks = [];

    /// <summary>
    ///     The declaration requirements collected from this builder, in the order they were added.
    /// </summary>
    private readonly List<DeclarationRequirement> _requiredDeclarations = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry shared across LiteBus modules.</param>
    /// <param name="contracts">The message contract registry for persisted inbox and outbox messages.</param>
    public MessageModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer used to register stable persisted contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets the optional time provider registered for messaging and dependent modules.
    /// </summary>
    internal TimeProvider? TimeProvider { get; private set; }

    /// <summary>
    ///     Gets the declaration requirements the composition check enforces.
    /// </summary>
    internal IReadOnlyList<DeclarationRequirement> RequiredDeclarations => _requiredDeclarations;

    /// <summary>
    ///     Gets the application composition checks to run once every module has been built.
    /// </summary>
    internal IReadOnlyList<Action<IMessageCatalog>> CompositionChecks => _compositionChecks;

    /// <summary>
    ///     Gets a value indicating whether an open generic pipeline handler must be named rather than scanned.
    /// </summary>
    internal bool ExplicitOpenGenericsRequired { get; private set; }

    /// <summary>
    ///     Gets what mediation records, or null when the defaults stand.
    /// </summary>
    internal MediationTelemetryOptions? Telemetry { get; private set; }

    /// <summary>
    ///     Requires every registered message to declare a value of type <typeparamref name="TValue" /> or to record an
    ///     exemption from it.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type each message must state a position on.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is what turns a written policy into a startup failure. "Every command states the permission it
    ///         requires" is otherwise enforced by code review, and a command that forgets is an unguarded use case
    ///         nobody notices. Requiring the declaration makes the omission a composition error naming every offender.
    ///     </para>
    ///     <para>
    ///         Unscoped, so it applies to commands, queries and events alike. That is usually too wide: requiring a
    ///         permission declaration of every message also demands one from every query, and the exemptions written to
    ///         satisfy it say nothing, which trains a team to treat a rationale as paperwork. Reach for
    ///         <see cref="RequireDeclaration{TValue,TScope}" /> or the predicate overload instead, and keep this one
    ///         for a value every message genuinely has to state.
    ///     </para>
    ///     <para>
    ///         A message satisfies the requirement by declaring the value through an attribute or a definition, or by
    ///         carrying a <see cref="DeclarationExemptAttribute" /> for that value type. A declaration inherited from a
    ///         base type or marker interface counts, so a family of messages can satisfy it once.
    ///     </para>
    ///     <para>
    ///         The check runs after every module has been built, because the messaging module is foundational and has
    ///         no commands or queries to inspect while it is being built. Abstract types and interfaces are skipped:
    ///         they are shapes rather than messages, and a declaration on one covers the messages beneath it.
    ///     </para>
    ///     <para>
    ///         Analyzer <c>LB1020</c> reports the same omission at compile time, message by message, and is the better
    ///         first line. Keep both: the analyzer catches it while writing the message, and this catches a message
    ///         registered from an assembly the analyzer never saw.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder RequireDeclaration<TValue>()
        where TValue : notnull
    {
        return AddRequirement(new DeclarationRequirement(typeof(TValue), Scope: null, "every registered message"));
    }

    /// <summary>
    ///     Requires every registered message assignable to <typeparamref name="TScope" /> to declare a value of type
    ///     <typeparamref name="TValue" /> or to record an exemption from it.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type each message in scope must state a position on.</typeparam>
    /// <typeparam name="TScope">
    ///     The axis contract or marker interface that decides the scope, such as <c>ICommand</c> or an application's
    ///     own <c>IActingAccountCommand</c>.
    /// </typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is the form to reach for. Scoping to <c>ICommand</c> stops a permission requirement from demanding
    ///         exemptions of every query, and scoping to an application's own marker expresses the rule that actually
    ///         holds: "every command that names an acting account declares what that account has to be permitted to
    ///         do" is a sentence a security review can read, and it is enforced against commands written after the
    ///         review.
    ///     </para>
    ///     <para>
    ///         The scope is a type rather than a namespace on purpose. A namespace is a string a refactoring tool moves
    ///         without telling anyone, so a requirement keyed on one silently stops applying when a folder is renamed,
    ///         and for an authorization rule that failure is an unguarded command that used to be guarded.
    ///     </para>
    ///     <para>
    ///         Requirements are declared here rather than on each axis builder so that every policy the composition
    ///         enforces reads from one place. Spreading them across the command, query, and event builders is what
    ///         makes a half-configured policy possible.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder RequireDeclaration<TValue, TScope>()
        where TValue : notnull
    {
        return AddRequirement(new DeclarationRequirement(
            typeof(TValue),
            static messageType => typeof(TScope).IsAssignableFrom(messageType),
            $"every {typeof(TScope).Name}"));
    }

    /// <summary>
    ///     Requires every registered message the predicate selects to declare a value of type
    ///     <typeparamref name="TValue" /> or to record an exemption from it.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type each message in scope must state a position on.</typeparam>
    /// <param name="scope">The predicate deciding whether a concrete message type is in scope.</param>
    /// <param name="scopeDescription">
    ///     What the scope is, in the words the composition error uses, such as <c>every command in the billing
    ///     module</c>. A predicate cannot describe itself, and an error that cannot name the policy it enforces is one
    ///     nobody can act on.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="scopeDescription" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     Prefer <see cref="RequireDeclaration{TValue,TScope}" /> where a marker type can express the scope, because
    ///     the compiler then tracks membership. Use this for a rule no type captures, such as one keyed on an attribute
    ///     the application defines.
    /// </remarks>
    public MessageModuleBuilder RequireDeclaration<TValue>(Func<Type, bool> scope, string scopeDescription)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeDescription);

        return AddRequirement(new DeclarationRequirement(typeof(TValue), scope, scopeDescription));
    }

    /// <summary>
    ///     Records one requirement, ignoring an exact duplicate.
    /// </summary>
    /// <param name="requirement">The requirement to enforce.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Two requirements over the same value type with different scopes both stand, because the wider one is not
    ///     implied by the narrower one. Only an identical repeat is dropped, which is what a composition file assembled
    ///     from several helpers produces.
    /// </remarks>
    private MessageModuleBuilder AddRequirement(DeclarationRequirement requirement)
    {
        if (!_requiredDeclarations.Contains(requirement))
        {
            _requiredDeclarations.Add(requirement);
        }

        return this;
    }

    /// <summary>
    ///     Runs an application composition check over every registered message, after every module has been built.
    /// </summary>
    /// <param name="validate">
    ///     The check. It receives the catalog of registered messages and their declarations, and throws to fail
    ///     composition.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validate" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         This is where an application's own conventions become startup failures. Unique audit action codes, a
    ///         house naming rule for them, a family of commands that all have to declare the same value: each is a
    ///         pure function of the declarations, and each is otherwise enforced by code review.
    ///     </para>
    ///     <para>
    ///         It runs at the same point <c>RequireDeclaration</c> does, after every module has built, so the catalog
    ///         holds every message the host composed rather than only the ones registered before this call.
    ///     </para>
    ///     <para>
    ///         Throw from the callback to fail composition. Prefer naming every offender in one message: a check that
    ///         reports the first problem makes a team fix a convention one restart at a time.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// messaging.ValidateComposition(catalog =>
    /// {
    ///     var wrong = catalog.Audited()
    ///         .Where(entry => !ActionCodePattern.IsMatch(entry.Audit!.Action))
    ///         .Select(entry => entry.MessageType.Name)
    ///         .ToList();
    ///
    ///     if (wrong.Count > 0)
    ///     {
    ///         throw new InvalidOperationException($"Audit actions must be category.kebab-action: {string.Join(", ", wrong)}");
    ///     }
    /// });
    /// ]]></code>
    /// </example>
    public MessageModuleBuilder ValidateComposition(Action<IMessageCatalog> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _compositionChecks.Add(validate);
        return this;
    }

    /// <summary>
    ///     Requires every audited message to declare an action code no other message declares.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Two messages sharing an action code make the trail unqueryable by use case, which is the one thing an action
    ///     code exists for. It is also the mistake copying a definition produces, and nothing else reports it: the
    ///     records are written, the column is populated, and the defect only surfaces when someone reads the trail and
    ///     finds two different operations under one name.
    /// </remarks>
    public MessageModuleBuilder RequireUniqueAuditActions()
    {
        return ValidateComposition(static catalog =>
        {
            var byAction = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            foreach (var entry in catalog.Audited())
            {
                if (!byAction.TryGetValue(entry.Audit!.Action, out var messages))
                {
                    messages = [];
                    byAction[entry.Audit.Action] = messages;
                }

                messages.Add(entry.MessageType.Name);
            }

            var duplicates = byAction
                .Where(static pair => pair.Value.Count > 1)
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"  '{pair.Key}' is declared by: {string.Join(", ", pair.Value)}")
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new AuditConfigurationException(
                    "Two or more audited messages declare the same audit action, so the trail cannot be queried by use case:"
                    + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
            }
        });
    }

    /// <summary>
    ///     Requires every audited action code to match a pattern.
    /// </summary>
    /// <param name="pattern">
    ///     The regular expression every action must match. Defaults to <c>category.kebab-action</c>, the convention the
    ///     LiteBus documentation uses throughout.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="pattern" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     An action code is read by whoever queries the trail years later, so a convention that holds is worth more
    ///     than one that mostly holds. Nothing else enforces it: an inconsistent code is written and stored exactly like
    ///     a consistent one.
    /// </remarks>
    public MessageModuleBuilder RequireAuditActionFormat(string pattern = DefaultAuditActionPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var expression = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        return ValidateComposition(catalog =>
        {
            var wrong = catalog.Audited()
                .Where(entry => !expression.IsMatch(entry.Audit!.Action))
                .Select(static entry => $"  {entry.MessageType.Name} declares '{entry.Audit!.Action}'")
                .OrderBy(static line => line, StringComparer.Ordinal)
                .ToList();

            if (wrong.Count > 0)
            {
                throw new AuditConfigurationException(
                    $"One or more audit actions do not match the required format '{pattern}':"
                    + Environment.NewLine + string.Join(Environment.NewLine, wrong));
            }
        });
    }

    /// <summary>
    ///     Requires every open generic pipeline handler to be named by the composition code rather than picked up by an
    ///     assembly scan.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         An open generic handler is closed over every registered message it fits, so adding one file to a scanned
    ///         assembly inserts a pipeline stage into every message in the application. That is convenient and it is
    ///         also the most powerful implicit behavior in the library: there is no registration line for a reviewer to
    ///         read, and the diff that caused it is a new file rather than a composition change.
    ///     </para>
    ///     <para>
    ///         Turning this on makes composition fail, naming each offender, until every one is registered explicitly
    ///         with <c>Register(typeof(MyHandler&lt;&gt;))</c>. Scanning still finds everything else.
    ///     </para>
    ///     <para>
    ///         It is opt-in rather than the default because picking up open generic handlers is what an assembly scan
    ///         has meant since v4, and changing that changes what a scan is rather than fixing a defect. The
    ///         composition summary reports every open generic and its closure count whether this is on or not, which is
    ///         the visibility the behavior actually lacked.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder RequireExplicitOpenGenerics()
    {
        ExplicitOpenGenericsRequired = true;
        return this;
    }

    /// <summary>
    ///     Configures what mediation records through OpenTelemetry.
    /// </summary>
    /// <param name="options">What to record.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         Metrics and one span per mediation are on without this call. Reach for it to turn on per-stage spans
    ///         and per-stage durations while investigating where mediation time goes, or to turn everything off in a
    ///         host that exports the same measurements through its own instrumentation.
    ///     </para>
    ///     <para>
    ///         Register the source and the meter with an exporter through
    ///         <c>LiteBus.Messaging.Extensions.OpenTelemetry</c>. Instruments with no listener record nothing whatever
    ///         is configured here.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseTelemetry(MediationTelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Telemetry = options;
        return this;
    }

    /// <summary>
    ///     Declares a metadata value for every message assignable to <typeparamref name="TScope" />, unless the
    ///     message states its own position.
    /// </summary>
    /// <typeparam name="TScope">
    ///     The base type or marker interface the family shares, such as an application's <c>IOrganizationCommand</c>.
    /// </typeparam>
    /// <typeparam name="TValue">The metadata value type, which is also its key.</typeparam>
    /// <param name="value">The value the family declares by default.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         "Everything under organizations requires ManageOrganization unless it says otherwise" is a rule worth
    ///         stating once. Declaring it on each of a hundred commands is the same rule stated a hundred times, and a
    ///         hundred places for it to drift.
    ///     </para>
    ///     <para>
    ///         Nothing new decides precedence. A declaration resolves to the one written closest to the message, so a
    ///         command carrying its own <typeparamref name="TValue" /> keeps it and everything else in the family
    ///         inherits this. That is the same rule a definition written for a base type has always followed; this is
    ///         a way to state it without a file.
    ///     </para>
    ///     <para>
    ///         The scope is a type rather than a namespace for the reason a scoped requirement is: a namespace is a
    ///         string a refactoring tool moves without telling anyone, so a default keyed on one silently stops
    ///         applying when a folder is renamed. For an authorization default, that failure is a command that used to
    ///         be guarded and now is not.
    ///     </para>
    ///     <para>
    ///         Two defaults for the same scope and value type are a configuration error, because one of them would
    ///         have to be discarded and nothing says which.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging
    ///     .DeclareDefault<IOrganizationCommand, RequiredAuthorization>(
    ///         new RequiredAuthorization(PermittedAction.ManageOrganization, Subject.Organization))
    ///     .RequireDeclaration<RequiredAuthorization, IOrganizationCommand>());
    /// ]]></code>
    /// </example>
    public MessageModuleBuilder DeclareDefault<TScope, TValue>(TValue value)
        where TScope : notnull
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(value);

        _messageRegistry.AddDeclaration(MessageDeclarationItem.For<TScope, TValue>(value));
        return this;
    }

    /// <summary>
    ///     Declares a metadata value for a message type, its base types, or a marker interface it implements.
    /// </summary>
    /// <param name="declaration">The message type the value covers, the type it is keyed by, and the value.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declaration" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for composition code that builds its declarations
    ///     from configuration, where the types are values rather than something the call site can name.
    /// </remarks>
    public MessageModuleBuilder DeclareDefault(MessageDeclarationItem declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        _messageRegistry.AddDeclaration(declaration);
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="TimeProvider" /> instance exposed through dependency injection.
    /// </summary>
    /// <param name="timeProvider">
    ///     The time provider to register. When omitted at build time,
    ///     <see cref="TimeProvider.System" /> is used.
    /// </param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        TimeProvider = timeProvider;
        return this;
    }

    /// <summary>
    ///     Gets the optional audit outcome mapper exposed through dependency injection.
    /// </summary>
    internal IAuditOutcomeMapper? AuditOutcomeMapper { get; private set; }

    /// <summary>
    ///     Gets the audit trail the module registers, as an instance or an implementation type.
    /// </summary>
    /// <value>
    ///     The instance passed to <see cref="UseAuditTrailInstance" />, the type passed to
    ///     <see cref="UseAuditTrail{TAuditTrail}" />, or <see langword="null" /> when the application registers the trail
    ///     with its own container instead.
    /// </value>
    internal object? AuditTrail { get; private set; }

    /// <summary>
    ///     Gets the lifetime the trail implementation type is registered with.
    /// </summary>
    internal InstanceLifetime AuditTrailLifetime { get; private set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Registers the <see cref="IAuditTrail" /> type that receives audit records, constructed by the container.
    /// </summary>
    /// <typeparam name="TAuditTrail">The trail implementation.</typeparam>
    /// <param name="lifetime">
    ///     The lifetime the trail is resolved with. Defaults to <see cref="InstanceLifetime.Scoped" />, which is what a
    ///     trail taking a database session needs.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is the one place the audit feature is plumbed: the trail here, the optional mapper through
    ///         <see cref="UseAuditOutcomeMapper" />, and the per-axis switch through <c>EnableAuditing</c> on the command
    ///         or query module, which decides which messages produce records.
    ///     </para>
    ///     <para>
    ///         The lifetime is a parameter rather than a consequence of which overload was reached for. A trail wrapping
    ///         a scoped database session is correct as <see cref="InstanceLifetime.Scoped" /> and captures one session
    ///         for the life of the process as a singleton, and nothing about the call site used to say which one you
    ///         were choosing.
    ///     </para>
    ///     <para>
    ///         Registering the trail with the application container instead still works, and the
    ///         <c>litebus.audit.trail</c> diagnostic check accepts either.
    ///     </para>
    ///     <para>
    ///         <see cref="InstanceLifetime" /> lives in <c>LiteBus.Runtime.Abstractions</c>, so overriding the default
    ///         needs that using directive in a registration file that touches nothing else in Runtime. The default is
    ///         the lifetime a trail usually wants, and <see cref="UseAuditTrailInstance" /> names the singleton case,
    ///         so the directive is only needed to say <see cref="InstanceLifetime.Transient" /> or to state
    ///         <see cref="InstanceLifetime.Scoped" /> explicitly.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditTrail<TAuditTrail>(InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditTrail : class, IAuditTrail
    {
        AuditTrail = typeof(TAuditTrail);
        AuditTrailLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditTrail" /> that receives audit records.
    /// </summary>
    /// <param name="auditTrail">The trail instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. A trail built here with a
    ///     database session captures that one session forever, which is the failure this name exists to make visible at
    ///     the call site. Use <see cref="UseAuditTrail{TAuditTrail}" /> whenever the trail has dependencies.
    /// </remarks>
    public MessageModuleBuilder UseAuditTrailInstance(IAuditTrail auditTrail)
    {
        ArgumentNullException.ThrowIfNull(auditTrail);
        AuditTrail = auditTrail;
        AuditTrailLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Gets the audit record writer the module registers, as an instance or an implementation type.
    /// </summary>
    /// <value>
    ///     The instance passed to <see cref="UseAuditRecordWriterInstance" />, the type passed to
    ///     <see cref="UseAuditRecordWriter{TAuditRecordWriter}" />, or <see langword="null" /> to use the built-in
    ///     writer.
    /// </value>
    internal object? AuditRecordWriter { get; private set; }

    /// <summary>
    ///     Gets the lifetime the audit record writer implementation type is registered with.
    /// </summary>
    internal InstanceLifetime AuditRecordWriterLifetime { get; private set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Replaces the writer that turns a completed mediation into an audit record.
    /// </summary>
    /// <typeparam name="TAuditRecordWriter">The writer implementation.</typeparam>
    /// <param name="lifetime">
    ///     The lifetime the writer is resolved with. Defaults to <see cref="InstanceLifetime.Scoped" />, which is what
    ///     a writer holding a database session needs.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         This is the seam for an application that wants its own audit record shape. <see cref="AuditRecord" /> is
    ///         a handoff, not a schema, so a different set of columns needs no new abstraction here: it needs a
    ///         different writer. Replacing it keeps the completion-stage placement, the
    ///         <see cref="HandlerPriorities.Observability" /> priority, and the per-axis wiring, and replaces exactly
    ///         the record building.
    ///     </para>
    ///     <para>
    ///         A replacement owns everything the built-in writer does, and every part of it is public: reading the
    ///         audit position through <see cref="IMessageRegistry" /> and <see cref="AuditDeclaration" />, enforcing
    ///         <see cref="AuditedDeclaration.ReasonRequired" /> with
    ///         <see cref="AuditReasonMissingException" />, reading what a handler supplied through
    ///         <see cref="IAuditScope" />, and classifying the outcome through <see cref="IAuditOutcomeMapper" />.
    ///         Skipping a message that declares no audit position is part of that contract: without it, every message
    ///         produces a record.
    ///     </para>
    ///     <para>
    ///         A custom writer does not have to use <see cref="IAuditTrail" />, so the <c>litebus.audit.trail</c>
    ///         probe stops asserting one is registered and reports the writer instead. LiteBus cannot know what a
    ///         writer it did not build needs.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditRecordWriter<TAuditRecordWriter>(
        InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditRecordWriter : class, IAuditRecordWriter
    {
        AuditRecordWriter = typeof(TAuditRecordWriter);
        AuditRecordWriterLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Replaces the audit record writer with a pre-created instance.
    /// </summary>
    /// <param name="auditRecordWriter">The writer instance, shared for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="auditRecordWriter" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. Use
    ///     <see cref="UseAuditRecordWriter{TAuditRecordWriter}" /> whenever the writer has dependencies.
    /// </remarks>
    public MessageModuleBuilder UseAuditRecordWriterInstance(IAuditRecordWriter auditRecordWriter)
    {
        ArgumentNullException.ThrowIfNull(auditRecordWriter);
        AuditRecordWriter = auditRecordWriter;
        AuditRecordWriterLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Gets the axis selection <see cref="AddAuditing" /> recorded, or null when it was not called.
    /// </summary>
    /// <remarks>
    ///     Published into the shared module context by the messaging module so that the command, query, and event
    ///     modules, which build after it, can register their own audit completion handler without the consumer
    ///     repeating the decision on each of their builders.
    /// </remarks>
    internal AuditingComposition? Auditing { get; private set; }

    /// <summary>
    ///     Configures the whole audit trail feature: the trail, the actor resolver, the outcome mapper, and which axes
    ///     produce records.
    /// </summary>
    /// <param name="configure">The auditing configuration callback.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         This is the one place the feature is decided. Before it, the trail lived here and the per-axis switch
    ///         lived on the command and query builders, so an application could register either without the other and
    ///         find out from a diagnostic probe. The per-axis switches remain and are what this composes.
    ///     </para>
    ///     <para>
    ///         Calling it twice adds to the same selection rather than replacing it, so an axis enabled once stays
    ///         enabled.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging.AddAuditing(auditing => auditing
    ///     .UseTrail<MartenAuditTrail>(InstanceLifetime.Scoped)
    ///     .UseActorResolver<RequestActorResolver>()
    ///     .ForCommands()
    ///     .ForQueries()));
    /// ]]></code>
    /// </example>
    public MessageModuleBuilder AddAuditing(Action<AuditingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Auditing ??= new AuditingComposition();
        configure(new AuditingBuilder(this, Auditing));

        return this;
    }

    /// <summary>
    ///     Gets the audit actor resolver the module registers, as an instance or an implementation type.
    /// </summary>
    internal object? AuditActorResolver { get; private set; }

    /// <summary>
    ///     Gets the lifetime the actor resolver implementation type is registered with.
    /// </summary>
    internal InstanceLifetime AuditActorResolverLifetime { get; private set; } = InstanceLifetime.Scoped;

    /// <summary>
    ///     Registers the <see cref="IAuditActorResolver" /> that says who an audited action is attributed to.
    /// </summary>
    /// <typeparam name="TAuditActorResolver">The resolver implementation.</typeparam>
    /// <param name="lifetime">
    ///     The lifetime the resolver is resolved with. Defaults to <see cref="InstanceLifetime.Scoped" />, which is
    ///     what a resolver reading the authenticated principal of the request in flight needs.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Without a resolver, every record is written with no actor, which makes the trail a log of what happened
    ///         rather than a record of who is answerable for it. The <c>litebus.audit.trail</c> probe reports the gap
    ///         at startup rather than leaving it to be noticed during a review.
    ///     </para>
    ///     <para>
    ///         The resolver runs at the completion stage, so it also attributes a denied or failed command. That is the
    ///         case a trail exists for and the reason this is not a pre-stage handler: a guard that denies stops the
    ///         pipeline before any pre-handler could have recorded who tried.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditActorResolver<TAuditActorResolver>(
        InstanceLifetime lifetime = InstanceLifetime.Scoped)
        where TAuditActorResolver : class, IAuditActorResolver
    {
        AuditActorResolver = typeof(TAuditActorResolver);
        AuditActorResolverLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditActorResolver" /> that says who an audited action is attributed to, named as
    ///     a type.
    /// </summary>
    /// <param name="resolverType">The resolver implementation, which must implement <see cref="IAuditActorResolver" />.</param>
    /// <param name="lifetime">The lifetime the resolver is resolved with.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resolverType" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="resolverType" /> does not implement <see cref="IAuditActorResolver" />, or cannot be
    ///     constructed.
    /// </exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for composition code that chooses the resolver at
    ///     runtime, where the type is a value rather than something the call site can name.
    /// </remarks>
    public MessageModuleBuilder UseAuditActorResolver(
        Type resolverType,
        InstanceLifetime lifetime = InstanceLifetime.Scoped)
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

        AuditActorResolver = resolverType;
        AuditActorResolverLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IAuditActorResolver" /> that says who an audited action is attributed to.
    /// </summary>
    /// <param name="auditActorResolver">The resolver instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. A resolver that reads
    ///     the request in flight through a scoped dependency cannot be built here; use
    ///     <see cref="UseAuditActorResolver{TAuditActorResolver}" /> for that.
    /// </remarks>
    public MessageModuleBuilder UseAuditActorResolverInstance(IAuditActorResolver auditActorResolver)
    {
        ArgumentNullException.ThrowIfNull(auditActorResolver);
        AuditActorResolver = auditActorResolver;
        AuditActorResolverLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditOutcomeMapper" /> instance used to classify how an audited action ended.
    /// </summary>
    /// <param name="auditOutcomeMapper">The mapper to register, shared for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         LiteBus knows that a mediation failed but cannot know whether it failed because the actor was not
    ///         permitted. Register a mapper so that an application refusal exception is recorded as
    ///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />. An application that
    ///         refuses through a guard or a validator needs no mapper: the pipeline already reports
    ///         <see cref="MediationOutcome.Denied" /> and <see cref="MediationOutcome.Invalid" /> as decisions.
    ///     </para>
    ///     <para>
    ///         When omitted, <c>DefaultAuditOutcomeMapper</c> maps each mediation outcome to the audit outcome that
    ///         matches it and records every remaining failure as <see cref="AuditOutcome.Failed" />.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseAuditOutcomeMapper(IAuditOutcomeMapper auditOutcomeMapper)
    {
        ArgumentNullException.ThrowIfNull(auditOutcomeMapper);
        AuditOutcomeMapper = auditOutcomeMapper;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IAuditOutcomeMapper" /> used to classify how an audited action ended.
    /// </summary>
    /// <typeparam name="TAuditOutcomeMapper">The mapper implementation, constructed once at configuration time.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The mapper is read on the completion path of every audited mediation and must stay pure, so it is created
    ///     once here rather than resolved per scope. A mapper needing scoped state is reading the wrong thing: the
    ///     completion context it is handed already carries the outcome, the exception, and the reason.
    /// </remarks>
    public MessageModuleBuilder UseAuditOutcomeMapper<TAuditOutcomeMapper>()
        where TAuditOutcomeMapper : IAuditOutcomeMapper, new()
    {
        AuditOutcomeMapper = new TAuditOutcomeMapper();
        return this;
    }

    /// <summary>
    ///     Registers a message or handler type with the message registry.
    /// </summary>
    /// <typeparam name="T">The type to register.</typeparam>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder Register<T>()
    {
        _messageRegistry.Register(typeof(T));

        return this;
    }

    /// <summary>
    ///     Registers a message or handler type with the message registry.
    /// </summary>
    /// <param name="type">The type to register.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder Register(Type type)
    {
        _messageRegistry.Register(type);
        return this;
    }

    /// <summary>
    ///     Registers all applicable types from an assembly with the message registry.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The current builder.</returns>
    public MessageModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes())
        {
            _messageRegistry.RegisterFromScan(type);
        }

        Contracts.AddFromAssembly(assembly);

        return this;
    }
}