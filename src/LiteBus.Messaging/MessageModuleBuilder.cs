using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiteBus.Messaging.Abstractions;

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
    ///     The per-type samples host validation round trips build through the default sample factory.
    /// </summary>
    private readonly MessageSampleRegistry _messageSamples = new();

    /// <summary>
    ///     The application composition checks to run once every module has been built.
    /// </summary>
    private readonly List<Action<ICompositionModel>> _compositionChecks = [];

    /// <summary>
    ///     The declaration requirements collected from this builder, in the order they were added.
    /// </summary>
    private readonly List<DeclarationRequirement> _requiredDeclarations = [];

    /// <summary>
    ///     The metadata conventions collected from this builder, in the order they were added.
    /// </summary>
    private readonly List<MessageDeclarationConvention> _conventions = [];

    /// <summary>
    ///     Whether the convention build step was registered; <see cref="DeclareConvention{TValue}" /> registers it once
    ///     for every convention.
    /// </summary>
    private bool _conventionsRegistered;

    /// <summary>
    ///     Whether the declaration report build step was registered; the first report-mode requirement registers it once
    ///     for every report-mode requirement.
    /// </summary>
    private bool _reportRegistered;

    /// <summary>
    ///     The fault refusal classifier implementation types and pre-created instances, in registration order.
    /// </summary>
    private readonly List<object> _faultRefusalClassifiers = [];

    /// <summary>
    ///     The refusal exception translator implementation types and pre-created instances, in registration order.
    /// </summary>
    private readonly List<object> _refusalExceptionTranslators = [];

    /// <summary>
    ///     Whether the translator build step was registered; the first translator registration registers it once
    ///     for every translator.
    /// </summary>
    private bool _refusalExceptionTranslatorsRegistered;

    /// <summary>
    ///     The registration steps that features in this assembly add to the module build, in the order they were added.
    /// </summary>
    private readonly List<Action<IModuleConfiguration>> _buildSteps = [];

    /// <summary>
    ///     Initializes a new instance of the <see cref="MessageModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry shared across LiteBus modules.</param>
    /// <param name="contracts">The message contract registry for persisted inbox and outbox messages.</param>
    /// <param name="refusals">The refusal catalog shared across LiteBus modules, or <see langword="null" /> for a fresh default catalog.</param>
    public MessageModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts, IRefusalCatalog? refusals = null)
    {
        _messageRegistry = messageRegistry;
        Contracts = contracts;
        Refusals = refusals ?? RefusalCatalog.CreateDefault();
    }

    /// <summary>
    ///     Gets the message contract writer used to register stable persisted contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets the refusal writer used to register transport-neutral refusal code meanings.
    /// </summary>
    public IRefusalWriter Refusals { get; }

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
    internal IReadOnlyList<Action<ICompositionModel>> CompositionChecks => _compositionChecks;

    /// <summary>
    ///     Gets the registration steps that features in this assembly add to the module build.
    /// </summary>
    internal IReadOnlyList<Action<IModuleConfiguration>> BuildSteps => _buildSteps;

    /// <summary>
    ///     Adds a registration step that runs when the message module builds, after the core messaging services and the
    ///     scanned handlers are registered.
    /// </summary>
    /// <param name="step">The step, which registers services, handlers or composition validations.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Feature extensions in this assembly, such as tenancy and authorization policies, use it so they do not edit
    ///     this builder for every feature.
    /// </remarks>
    internal MessageModuleBuilder AddBuildStep(Action<IModuleConfiguration> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _buildSteps.Add(step);
        return this;
    }

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
    /// <param name="enforcement">
    ///     <see cref="DeclarationEnforcement.Fail" /> fails composition, naming every offender.
    ///     <see cref="DeclarationEnforcement.Report" /> lets composition succeed and lists the offenders through the
    ///     <c>litebus.declarations</c> probe instead, so positions are adopted message by message.
    /// </param>
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
    public MessageModuleBuilder RequireDeclaration<TValue>(
        DeclarationEnforcement enforcement = DeclarationEnforcement.Fail)
        where TValue : notnull
    {
        return AddRequirement(new DeclarationRequirement(
            typeof(TValue),
            Scope: null,
            "every registered message",
            enforcement));
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
    /// <param name="enforcement">
    ///     <see cref="DeclarationEnforcement.Fail" /> fails composition, naming every offender.
    ///     <see cref="DeclarationEnforcement.Report" /> lets composition succeed and lists the offenders through the
    ///     <c>litebus.declarations</c> probe instead, so positions are adopted message by message.
    /// </param>
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
    public MessageModuleBuilder RequireDeclaration<TValue, TScope>(
        DeclarationEnforcement enforcement = DeclarationEnforcement.Fail)
        where TValue : notnull
    {
        return AddRequirement(new DeclarationRequirement(
            typeof(TValue),
            static messageType => typeof(TScope).IsAssignableFrom(messageType),
            $"every {typeof(TScope).Name}",
            enforcement));
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
    /// <param name="enforcement">
    ///     <see cref="DeclarationEnforcement.Fail" /> fails composition, naming every offender.
    ///     <see cref="DeclarationEnforcement.Report" /> lets composition succeed and lists the offenders through the
    ///     <c>litebus.declarations</c> probe instead, so positions are adopted message by message.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException"><paramref name="scopeDescription" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     Prefer <see cref="RequireDeclaration{TValue,TScope}" /> where a marker type can express the scope, because
    ///     the compiler then tracks membership. Use this for a rule no type captures, such as one keyed on an attribute
    ///     the application defines.
    /// </remarks>
    public MessageModuleBuilder RequireDeclaration<TValue>(
        Func<Type, bool> scope,
        string scopeDescription,
        DeclarationEnforcement enforcement = DeclarationEnforcement.Fail)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeDescription);

        return AddRequirement(new DeclarationRequirement(typeof(TValue), scope, scopeDescription, enforcement));
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

        if (requirement.Enforcement == DeclarationEnforcement.Report && !_reportRegistered)
        {
            _reportRegistered = true;
            AddBuildStep(RegisterDeclarationReport);
        }

        return this;
    }

    /// <summary>
    ///     Registers the declaration report and its diagnostic probe for report-mode requirements.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the registration.</param>
    /// <remarks>
    ///     The report holds the requirements; the probe reads the message catalog at check time, so the list reflects
    ///     every message the host composed rather than only the ones registered before this call. Registered once for
    ///     every report-mode requirement, because one probe lists every requirement's offenders.
    /// </remarks>
    private void RegisterDeclarationReport(IModuleConfiguration configuration)
    {
        var reported = _requiredDeclarations
            .Where(requirement => requirement.Enforcement == DeclarationEnforcement.Report)
            .ToList();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(DeclarationReport),
            new DeclarationReport(reported)));
        configuration.RegisterDiagnosticCheck(
            typeof(DeclarationReportDiagnosticCheck),
            DeclarationReportDiagnosticCheck.CheckName);
    }

    /// <summary>
    ///     Runs an application composition check over everything the host composed, after every module has been built.
    /// </summary>
    /// <param name="validate">
    ///     The check. It receives the composition model: the catalog of registered messages and their declarations,
    ///     the declaration provenance per message, the declared and registered policy names, the contract
    ///     registrations, the resolved port implementations, and the contributed feature sections. It throws to fail
    ///     composition.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="validate" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         This is where an application's own conventions become startup failures. Unique audit action codes, a
    ///         house naming rule for them, a family of commands that all have to declare the same value, a message
    ///         declaring a policy name nothing registered: each is a pure function of the composition, and each is
    ///         otherwise enforced by code review.
    ///     </para>
    ///     <para>
    ///         It runs at the same point <c>RequireDeclaration</c> does, after every module has built, so the model
    ///         holds everything the host composed rather than only what was registered before this call. Ports are
    ///         reported as LiteBus composed them; a replacement the application registers directly with its own
    ///         container afterwards is visible on the model resolved from the container instead.
    ///     </para>
    ///     <para>
    ///         Throw from the callback to fail composition. Prefer naming every offender in one message: a check that
    ///         reports the first problem makes a team fix a convention one restart at a time.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// messaging.ValidateComposition(model =>
    /// {
    ///     var wrong = model.Messages.Audited()
    ///         .Where(entry => !ActionCodePattern.IsMatch(entry.GetAudit()!.Action))
    ///         .Select(entry => entry.MessageType.Name)
    ///         .ToList();
    ///
    ///     if (wrong.Count > 0)
    ///     {
    ///         throw new InvalidOperationException($"Audit actions must be category.kebab-action: {string.Join(", ", wrong)}");
    ///     }
    ///
    ///     UnknownPolicyCheck.ThrowIfUnknown(model.Messages, model.RegisteredPolicyNames);
    /// });
    /// ]]></code>
    /// </example>
    public MessageModuleBuilder ValidateComposition(Action<ICompositionModel> validate)
    {
        ArgumentNullException.ThrowIfNull(validate);
        _compositionChecks.Add(validate);
        return this;
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
    ///     Produces a metadata value from the message type itself, for every message that states no value of type
    ///     <typeparamref name="TValue" /> through a definition, an attribute, or a default.
    /// </summary>
    /// <typeparam name="TValue">The metadata value type the convention produces.</typeparam>
    /// <param name="convention">
    ///     Produces the value for a message type, or <see langword="null" /> when the convention does not apply to
    ///     it.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="convention" /> is <see langword="null" />.</exception>
    /// <exception cref="PipelineContractException">
    ///     A convention for <typeparamref name="TValue" /> is already declared. One value type has one convention.
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         A default says what a family declares; a convention says how a value is derived. An idempotency key
    ///         read from an interface every command implements, or an audit action derived from the type name, is
    ///         stated once here instead of repeated on every message, and a message that states its own position keeps
    ///         it: precedence stays definition, then attribute, then default, then convention.
    ///     </para>
    ///     <para>
    ///         A message that records an exemption from <typeparamref name="TValue" /> is left alone. An exemption is
    ///         a decision that the message deliberately states nothing, and a derived value would silently overturn it.
    ///     </para>
    ///     <para>
    ///         The function observes the message type only. It runs once per registered message at composition, after
    ///         every module has registered its messages and before the declaration requirements are checked, so a
    ///         convention value satisfies a requirement. Keep it a pure function of the type: it cannot resolve
    ///         services, and a function that throws fails composition.
    ///     </para>
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddMessaging(messaging => messaging
    ///     .DeclareConvention<IdempotencyDeclaration>(messageType =>
    ///         typeof(IHasIdempotencyKey).IsAssignableFrom(messageType)
    ///             ? IdempotencyDeclaration.KeyedBy<IHasIdempotencyKey>(message => message.IdempotencyKey)
    ///             : null)
    ///     .DeclareConvention<AuditDeclaration>(messageType =>
    ///         AuditDeclaration.Audited("commands." + messageType.Name)));
    /// ]]></code>
    /// </example>
    public MessageModuleBuilder DeclareConvention<TValue>(Func<Type, TValue?> convention)
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(convention);

        foreach (var existing in _conventions)
        {
            if (existing.ValueType == typeof(TValue))
            {
                throw new PipelineContractException(
                    $"A convention for '{typeof(TValue).Name}' is already declared. One value type has one "
                    + "convention, because two functions producing the value would leave nothing saying which one "
                    + "applies; combine them into one function that returns the value or null.");
            }
        }

        _conventions.Add(new MessageDeclarationConvention(typeof(TValue), type => convention(type)));

        if (!_conventionsRegistered)
        {
            _conventionsRegistered = true;
            AddBuildStep(ApplyConventions);
        }

        return this;
    }

    /// <summary>
    ///     Registers the composition validation that applies conventions to messages stating no value of their type.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the validation.</param>
    /// <remarks>
    ///     Registered as a build step rather than applied here because the axis modules register their messages after
    ///     the messaging module builds. Build steps run before the requirement validation is registered, so the
    ///     conventions land before the requirements are checked.
    /// </remarks>
    private void ApplyConventions(IModuleConfiguration configuration)
    {
        var registry = configuration.GetContext<IMessageRegistry>();
        var conventions = _conventions.ToList();

        configuration.RegisterCompositionValidation(() => ApplyConventions(registry, conventions));
    }

    /// <summary>
    ///     Applies every convention to every concrete message that states no value of the convention's type and
    ///     records no exemption from it.
    /// </summary>
    /// <param name="reader">The registry holding every registered message descriptor.</param>
    /// <param name="conventions">The conventions to apply, in registration order.</param>
    private static void ApplyConventions(IMessageReader reader, IReadOnlyList<MessageDeclarationConvention> conventions)
    {
        foreach (var descriptor in reader)
        {
            if (descriptor.MessageType.IsAbstract || descriptor.MessageType.IsInterface)
            {
                continue;
            }

            foreach (var convention in conventions)
            {
                if (StatesValue(descriptor.Metadata, convention.ValueType)
                    || IsExempt(descriptor.Metadata, convention.ValueType))
                {
                    continue;
                }

                var value = convention.Convention(descriptor.MessageType);

                if (value is null)
                {
                    continue;
                }

                // The messaging module always pairs this builder with MessageRegistry, whose descriptors carry
                // MessageMetadata, so the cast holds for every descriptor this validation sees.
                ((MessageMetadata) descriptor.Metadata).TryApplyConvention(
                    convention.ValueType,
                    value,
                    descriptor.MessageType);
            }
        }
    }

    /// <summary>
    ///     Determines whether the metadata holds a value of the convention's type.
    /// </summary>
    /// <param name="metadata">The metadata resolved for one message.</param>
    /// <param name="valueType">The metadata value type the convention produces.</param>
    /// <returns><see langword="true" /> when a value of that type is present.</returns>
    private static bool StatesValue(IMessageMetadata metadata, Type valueType)
    {
        return metadata.Values.Any(valueType.IsInstanceOfType);
    }

    /// <summary>
    ///     Determines whether the message deliberately states nothing for the convention's type.
    /// </summary>
    /// <param name="metadata">The metadata resolved for one message.</param>
    /// <param name="valueType">The metadata value type the convention produces.</param>
    /// <returns><see langword="true" /> when an exemption covers that type.</returns>
    private static bool IsExempt(IMessageMetadata metadata, Type valueType)
    {
        return metadata.TryGet<DeclarationExemptions>(out var exemptions) && exemptions.Covers(valueType);
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
    ///         A default applies only to messages assignable to its scope and never satisfies another scope's
    ///         requirement.
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
    ///     from configuration, where the types are values rather than something the call site can name. A default
    ///     applies only to messages assignable to its scope and never satisfies another scope's requirement.
    /// </remarks>
    public MessageModuleBuilder DeclareDefault(MessageDeclarationItem declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        _messageRegistry.AddDeclaration(declaration);
        return this;
    }

    /// <summary>
    ///     Declares a metadata value for every message assignable to <typeparamref name="TScope" />, overwriting any
    ///     declaration already recorded for the same scope and value type.
    /// </summary>
    /// <typeparam name="TScope">
    ///     The base type or marker interface the family shares, such as an application's <c>IOrganizationCommand</c>.
    /// </typeparam>
    /// <typeparam name="TValue">The metadata value type, which is also its key.</typeparam>
    /// <param name="value">The value the family declares.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     This is the deliberate override for the registration conflict rule. A conflicting repeat through
    ///     <see cref="DeclareDefault{TScope, TValue}" /> fails composition naming both sources, while this method
    ///     overwrites the recorded value.
    /// </remarks>
    public MessageModuleBuilder ReplaceDeclaration<TScope, TValue>(TValue value)
        where TScope : notnull
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(value);

        _messageRegistry.ReplaceDeclaration(MessageDeclarationItem.For<TScope, TValue>(value));
        return this;
    }

    /// <summary>
    ///     Declares a metadata value for a message type, overwriting any declaration already recorded for the same
    ///     message and value type.
    /// </summary>
    /// <param name="declaration">The message type the value covers, the type it is keyed by, and the value.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declaration" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for composition code that builds its declarations
    ///     from configuration, where the types are values rather than something the call site can name.
    /// </remarks>
    public MessageModuleBuilder ReplaceDeclaration(MessageDeclarationItem declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        _messageRegistry.ReplaceDeclaration(declaration);
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
    ///     Gets the message serializer the module registers, as an instance or an implementation type.
    /// </summary>
    /// <value>
    ///     The instance passed to <see cref="UseSerializerInstance" />, the type passed to
    ///     <see cref="UseSerializer{TMessageSerializer}" />, or <see langword="null" /> to use
    ///     <see cref="SystemTextJsonMessageSerializer" /> with web defaults.
    /// </value>
    internal object? Serializer { get; private set; }

    /// <summary>
    ///     Gets the lifetime a serializer implementation type is registered with.
    /// </summary>
    internal InstanceLifetime SerializerLifetime { get; private set; } = InstanceLifetime.Singleton;

    /// <summary>
    ///     Registers the <see cref="IMessageSerializer" /> that produces and reads persisted message payloads.
    /// </summary>
    /// <typeparam name="TMessageSerializer">The serializer implementation to construct from the container.</typeparam>
    /// <param name="lifetime">The lifetime the implementation is registered with. Singleton by default.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         The serializer decides the wire format of every durable message, so it is a compose-time decision like
    ///         <see cref="UseTimeProvider" /> and belongs here rather than in a container registration that replaces
    ///         the default afterwards. A replacement made outside composition is invisible to composition checks, and
    ///         forgetting it renames every property of every stored message without failing at startup.
    ///     </para>
    ///     <para>
    ///         The default is <see cref="SystemTextJsonMessageSerializer" /> with
    ///         <see cref="System.Text.Json.JsonSerializerDefaults.Web" />, which is camel case. Applications whose
    ///         published payloads are read by other systems should state the format here rather than inherit it.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseSerializer<TMessageSerializer>(InstanceLifetime lifetime = InstanceLifetime.Singleton)
        where TMessageSerializer : class, IMessageSerializer
    {
        Serializer = typeof(TMessageSerializer);
        SerializerLifetime = lifetime;
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IMessageSerializer" /> that produces and reads persisted message payloads.
    /// </summary>
    /// <param name="messageSerializer">The serializer instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The overload to reach for when the serializer is configured rather than injected, which is the common case:
    ///     <c>UseSerializerInstance(new SystemTextJsonMessageSerializer(options))</c> states the wire format at the
    ///     composition root where the rest of the durable configuration already lives.
    /// </remarks>
    public MessageModuleBuilder UseSerializerInstance(IMessageSerializer messageSerializer)
    {
        ArgumentNullException.ThrowIfNull(messageSerializer);
        Serializer = messageSerializer;
        SerializerLifetime = InstanceLifetime.Singleton;
        return this;
    }

    /// <summary>
    ///     Gets the JSON serializer options source the module registers, as options, a provider instance, or a
    ///     provider implementation type.
    /// </summary>
    /// <value>
    ///     The options passed to <see cref="UseSerializerOptions" />, the instance passed to
    ///     <see cref="UseSerializerOptionsProviderInstance" />, the type passed to
    ///     <see cref="UseSerializerOptionsProvider{TProvider}" />, or <see langword="null" /> to use
    ///     <see cref="DefaultMessageJsonSerializerOptionsProvider" />.
    /// </value>
    internal object? SerializerOptionsProvider { get; private set; }

    /// <summary>
    ///     Gets the message sample factory the module registers, as an instance or an implementation type.
    /// </summary>
    /// <value>
    ///     The instance passed to <see cref="UseMessageSampleFactoryInstance" />, the type passed to
    ///     <see cref="UseMessageSampleFactory{TFactory}" />, or <see langword="null" /> to use
    ///     <see cref="DefaultMessageSampleFactory" />.
    /// </value>
    internal object? MessageSampleFactory { get; private set; }

    /// <summary>
    ///     Registers the JSON serializer options every durable feature serializes message payloads with.
    /// </summary>
    /// <param name="options">The shared options. Outbox, inbox, and saga state all serialize through them.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         The simple overload over a constant
    ///         <see cref="IMessageJsonSerializerOptionsProvider" />: one call at the composition root states the wire
    ///         format for every durable payload instead of configuring one serializer per feature. The default is web
    ///         defaults, which is camel case.
    ///     </para>
    ///     <para>
    ///         Setting both a custom serializer through <see cref="UseSerializer{TMessageSerializer}" /> or
    ///         <see cref="UseSerializerInstance" /> and custom options fails composition: two payload formats with
    ///         nothing saying which applies.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseSerializerOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        SerializerOptionsProvider = options;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IMessageJsonSerializerOptionsProvider" /> that supplies the JSON serializer
    ///     options every durable feature serializes message payloads with.
    /// </summary>
    /// <typeparam name="TProvider">The provider implementation to construct from the container.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The overload to reach for when the options are built from configuration or other services. For fixed
    ///     options, <see cref="UseSerializerOptions" /> states them directly.
    /// </remarks>
    public MessageModuleBuilder UseSerializerOptionsProvider<TProvider>()
        where TProvider : class, IMessageJsonSerializerOptionsProvider
    {
        SerializerOptionsProvider = typeof(TProvider);
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IMessageJsonSerializerOptionsProvider" /> that supplies the JSON
    ///     serializer options every durable feature serializes message payloads with.
    /// </summary>
    /// <param name="provider">The provider instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider" /> is <see langword="null" />.</exception>
    public MessageModuleBuilder UseSerializerOptionsProviderInstance(IMessageJsonSerializerOptionsProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        SerializerOptionsProvider = provider;
        return this;
    }

    /// <summary>
    ///     Registers the <see cref="IMessageSampleFactory" /> that creates sample message instances for host
    ///     validation round-trip checks.
    /// </summary>
    /// <typeparam name="TFactory">The factory implementation to construct from the container.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The overload to reach for for message types without a parameterless shape or with invariants sample
    ///     construction must honor. A factory may delegate to <see cref="DefaultMessageSampleFactory" /> for the
    ///     types it does not handle itself.
    /// </remarks>
    public MessageModuleBuilder UseMessageSampleFactory<TFactory>()
        where TFactory : class, IMessageSampleFactory
    {
        MessageSampleFactory = typeof(TFactory);
        return this;
    }

    /// <summary>
    ///     Registers a pre-created <see cref="IMessageSampleFactory" /> that creates sample message instances for
    ///     host validation round-trip checks.
    /// </summary>
    /// <param name="factory">The factory instance, shared by every validation run for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is <see langword="null" />.</exception>
    public MessageModuleBuilder UseMessageSampleFactoryInstance(IMessageSampleFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        MessageSampleFactory = factory;
        return this;
    }

    /// <summary>
    ///     Gets the per-type samples host validation round trips build through the default sample factory.
    /// </summary>
    internal MessageSampleRegistry MessageSamples => _messageSamples;

    /// <summary>
    ///     Registers the sample host validation round trips build for one type.
    /// </summary>
    /// <typeparam name="TMessage">The sampled type: a message or a member type such as a value object.</typeparam>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="callerFile">The registering file, supplied by the compiler.</param>
    /// <param name="callerLine">The registering line, supplied by the compiler.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sample" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     <para>
    ///         The overload to reach for for one strict value object in an otherwise samplable domain, such as an
    ///         address validated against a regular expression. The default factory consults the sample at every
    ///         level of sampling, so one registration fixes every message holding the type.
    ///     </para>
    ///     <para>
    ///         An identical repeat is a no-op. A conflicting repeat fails composition naming both call sites, and
    ///         <see cref="ReplaceSample{TMessage}" /> overwrites deliberately. Registering a sample together with
    ///         a custom <see cref="IMessageSampleFactory" /> fails composition, because the custom factory would
    ///         silently ignore the sample.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder RegisterSample<TMessage>(
        Func<TMessage> sample,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(sample);

        _messageSamples.Add(sample, DescribeCaller(callerFile, callerLine));
        return this;
    }

    /// <summary>
    ///     Registers the sample host validation round trips build for one type.
    /// </summary>
    /// <param name="messageType">The sampled type: a message or a member type such as a value object.</param>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="callerFile">The registering file, supplied by the compiler.</param>
    /// <param name="callerLine">The registering line, supplied by the compiler.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="messageType" /> or <paramref name="sample" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for composition code that registers samples
    ///     from configuration, where the types are values rather than something the call site can name. An
    ///     identical repeat is a no-op, a conflicting repeat fails composition naming both call sites, and
    ///     <c>ReplaceSample</c> overwrites deliberately.
    /// </remarks>
    public MessageModuleBuilder RegisterSample(
        Type messageType,
        Func<object?> sample,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(sample);

        _messageSamples.Add(messageType, sample, DescribeCaller(callerFile, callerLine));
        return this;
    }

    /// <summary>
    ///     Registers the sample host validation round trips build for one type, overwriting any sample already
    ///     registered for it.
    /// </summary>
    /// <typeparam name="TMessage">The sampled type: a message or a member type such as a value object.</typeparam>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="callerFile">The registering file, supplied by the compiler.</param>
    /// <param name="callerLine">The registering line, supplied by the compiler.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sample" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     This is the deliberate override for the registration conflict rule. A conflicting repeat through
    ///     <see cref="RegisterSample{TMessage}" /> fails composition naming both call sites, while this method
    ///     overwrites the recorded sample.
    /// </remarks>
    public MessageModuleBuilder ReplaceSample<TMessage>(
        Func<TMessage> sample,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0)
        where TMessage : notnull
    {
        ArgumentNullException.ThrowIfNull(sample);

        _messageSamples.Replace(sample, DescribeCaller(callerFile, callerLine));
        return this;
    }

    /// <summary>
    ///     Registers the sample host validation round trips build for one type, overwriting any sample already
    ///     registered for it.
    /// </summary>
    /// <param name="messageType">The sampled type: a message or a member type such as a value object.</param>
    /// <param name="sample">The factory answering the sample.</param>
    /// <param name="callerFile">The registering file, supplied by the compiler.</param>
    /// <param name="callerLine">The registering line, supplied by the compiler.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="messageType" /> or <paramref name="sample" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     The generic overload is the one to reach for. This exists for composition code that registers samples
    ///     from configuration, where the types are values rather than something the call site can name.
    /// </remarks>
    public MessageModuleBuilder ReplaceSample(
        Type messageType,
        Func<object?> sample,
        [CallerFilePath] string? callerFile = null,
        [CallerLineNumber] int callerLine = 0)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(sample);

        _messageSamples.Replace(messageType, sample, DescribeCaller(callerFile, callerLine));
        return this;
    }

    /// <summary>
    ///     Describes the call site that registered a sample.
    /// </summary>
    /// <param name="callerFile">The registering file, supplied by the compiler.</param>
    /// <param name="callerLine">The registering line, supplied by the compiler.</param>
    /// <returns>The file name and line, so conflict messages and the snapshot stay machine-neutral.</returns>
    private static string DescribeCaller(string? callerFile, int callerLine)
    {
        if (string.IsNullOrWhiteSpace(callerFile))
        {
            return "composition";
        }

        return $"{Path.GetFileName(callerFile)}:{callerLine}";
    }

    /// <summary>
    ///     Gets the fault refusal classifiers registered through <see cref="UseFaultRefusalClassifier{TClassifier}" />
    ///     and <see cref="UseFaultRefusalClassifierInstance" />, in registration order, as implementation types and
    ///     pre-created instances.
    /// </summary>
    internal IReadOnlyList<object> FaultRefusalClassifiers => _faultRefusalClassifiers;

    /// <summary>
    ///     Registers a classifier that turns a recognized fault into the refusal the mediation ends as.
    /// </summary>
    /// <typeparam name="TClassifier">The classifier implementation, constructed from the container.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Call this once per classifier. The strategies consult them in registration order and the first match
    ///         wins, so register the most specific classifier first. A fault from any stage is offered, including the
    ///         completion stage, where a duplicate key or concurrency fault raised by a unit-of-work commit becomes a
    ///         typed refusal instead of an exception every application maps by hand.
    ///     </para>
    ///     <para>
    ///         Classifiers are singletons, so an implementation must be stateless and safe to call from concurrent
    ///         mediations, and may only depend on singleton services. Cancellation is never offered, and a classified
    ///         fault skips the error handlers: it is a refusal, not a fault.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseFaultRefusalClassifier<TClassifier>()
        where TClassifier : class, IFaultRefusalClassifier
    {
        _faultRefusalClassifiers.Add(typeof(TClassifier));
        return this;
    }

    /// <summary>
    ///     Registers a pre-created classifier that turns a recognized fault into the refusal the mediation ends as.
    /// </summary>
    /// <param name="classifier">The classifier instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="classifier" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. This is the overload
    ///     for a classifier that is configured rather than injected, such as one built over a constraint map. Use
    ///     <see cref="UseFaultRefusalClassifier{TClassifier}" /> whenever the classifier has dependencies.
    /// </remarks>
    public MessageModuleBuilder UseFaultRefusalClassifierInstance(IFaultRefusalClassifier classifier)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        _faultRefusalClassifiers.Add(classifier);
        return this;
    }

    /// <summary>
    ///     Registers a translator that turns a recognized domain exception into the refusal the mediation ends as.
    /// </summary>
    /// <typeparam name="TTranslator">The translator implementation, constructed from the dispatch scope.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         Call this once per translator. The strategies consult them in registration order and the first match
    ///         wins, so register the most specific translator first. A fault is offered here only when the fault
    ///         refusal classifiers left it unrecognized, and a translated fault skips the error handlers: it is a
    ///         refusal, not a fault.
    ///     </para>
    ///     <para>
    ///         Translators are scoped, so an implementation may read scoped services such as localization or
    ///         tenant-aware detail while translating. Cancellation, an exception that already carries a refusal, and a
    ///         <see cref="MessageFaultException" /> are never offered.
    ///     </para>
    /// </remarks>
    public MessageModuleBuilder UseRefusalExceptionTranslator<TTranslator>()
        where TTranslator : class, IRefusalExceptionTranslator
    {
        _refusalExceptionTranslators.Add(typeof(TTranslator));
        EnsureRefusalExceptionTranslatorRegistration();
        return this;
    }

    /// <summary>
    ///     Registers a pre-created translator that turns a recognized domain exception into the refusal the mediation
    ///     ends as.
    /// </summary>
    /// <param name="translator">The translator instance, shared by every mediation for the life of the process.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="translator" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The name says the lifetime, because a pre-created instance can only be a singleton. This is the overload
    ///     for a translator that is configured rather than injected. Use
    ///     <see cref="UseRefusalExceptionTranslator{TTranslator}" /> whenever the translator has dependencies.
    /// </remarks>
    public MessageModuleBuilder UseRefusalExceptionTranslatorInstance(IRefusalExceptionTranslator translator)
    {
        ArgumentNullException.ThrowIfNull(translator);
        _refusalExceptionTranslators.Add(translator);
        EnsureRefusalExceptionTranslatorRegistration();
        return this;
    }

    /// <summary>
    ///     Registers the build step that publishes the collected translators, once for every translator.
    /// </summary>
    /// <remarks>
    ///     Registered as a build step rather than applied here because translators collected after this call must be
    ///     published too: the step reads the collected entries when the message module builds, so registration order
    ///     is preserved whatever the call order was.
    /// </remarks>
    private void EnsureRefusalExceptionTranslatorRegistration()
    {
        if (_refusalExceptionTranslatorsRegistered)
        {
            return;
        }

        _refusalExceptionTranslatorsRegistered = true;
        AddBuildStep(RegisterRefusalExceptionTranslators);
    }

    /// <summary>
    ///     Registers the collected refusal exception translators as a multi-registration collection resolved in
    ///     registration order.
    /// </summary>
    /// <param name="configuration">The module configuration receiving the registrations.</param>
    /// <remarks>
    ///     Each entry is registered as one collection item rather than as the service itself, so several translators
    ///     coexist and the strategies resolve them as an ordered enumerable. Implementation types resolve scoped,
    ///     because a translator may read scoped services while translating. With no entry nothing is registered, and
    ///     every fault keeps the behavior it had before translators existed.
    /// </remarks>
    private void RegisterRefusalExceptionTranslators(IModuleConfiguration configuration)
    {
        foreach (var entry in _refusalExceptionTranslators)
        {
            if (entry is Type translatorType)
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(IRefusalExceptionTranslator),
                    translatorType,
                    InstanceLifetime.Scoped));
            }
            else
            {
                configuration.DependencyRegistry.RegisterCollection(DependencyDescriptor.ForCollection(
                    typeof(IRefusalExceptionTranslator),
                    entry));
            }
        }
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

        foreach (var type in AssemblyTypeScanner.GetLoadableTypes(assembly))
        {
            _messageRegistry.RegisterFromScan(type);
        }

        Contracts.AddFromAssembly(assembly);

        return this;
    }
}
