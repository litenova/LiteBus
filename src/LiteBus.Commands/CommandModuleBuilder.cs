using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Idempotency;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Commands;

/// <summary>
///     Builder class for registering command types in the message registry.
/// </summary>
public sealed class CommandModuleBuilder
{
    /// <summary>
    ///     Command handler contracts accepted by command-specific discovery.
    /// </summary>
    private static readonly HashSet<Type> HandlerContracts =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(ICommandPreHandler),
        typeof(ICommandPreHandler<>),
        typeof(ICommandGuard<>),
        typeof(ICommandValidator<>),
        typeof(ICommandShortcut<>),
        typeof(ICommandShortcut<,>),
        typeof(ICommandRefusalMapper<,>),
        typeof(ICommandPostHandler),
        typeof(ICommandPostHandler<>),
        typeof(ICommandPostHandler<,>),
        typeof(ICommandErrorHandler),
        typeof(ICommandErrorHandler<>),
        typeof(ICommandErrorHandler<,>),
        typeof(ICommandCompletionHandler),
        typeof(ICommandCompletionHandler<>),
        typeof(ICommandCompletionHandler<,>)
    ];

    /// <summary>
    ///     Deferred contract registrations, applied when the module builds.
    /// </summary>
    private readonly MessageContractBuilder _contracts = new();

    /// <summary>
    ///     Command construct types registered one at a time, in call order.
    /// </summary>
    private readonly List<Type> _types = [];

    /// <summary>
    ///     Assemblies scanned for command constructs, in call order.
    /// </summary>
    private readonly List<Assembly> _assemblies = [];

    /// <summary>
    ///     Child modules declared by feature packages composing through this builder.
    /// </summary>
    private readonly List<IModule> _childModules = [];

    /// <summary>
    ///     Gets the message contract writer for persisted command contracts.
    /// </summary>
    /// <remarks>
    ///     Registrations are collected here and applied to the shared registry when the module builds, because the
    ///     configuration action now runs while the module graph is still being declared and the live registry does not
    ///     exist yet.
    /// </remarks>
    public IContractWriter Contracts => _contracts;

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnableAuditing" /> was called.
    /// </summary>
    /// <remarks>
    ///     The module reads this after the configuration action runs, so it can register the diagnostic probe that reports
    ///     a missing <see cref="IAuditTrail" /> before the first audited mediation fails inside the completion stage.
    /// </remarks>
    internal bool AuditingEnabled { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnableIdempotency" /> was called.
    /// </summary>
    /// <remarks>
    ///     The module reads this after the configuration action runs, so it can register the diagnostic probe that
    ///     reports a missing <c>IIdempotencyStore</c> before the first declaring command arrives.
    /// </remarks>
    internal bool IdempotencyEnabled { get; private set; }

    /// <summary>
    ///     Registers the LiteBus command audit writer, so every command mediation produces an audit record when the
    ///     message declares one.
    /// </summary>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The writer runs at the completion stage, so refusals, failures and cancellations are recorded as well as
    ///         successes. A message is recorded only when it declares an audited position through
    ///         <see cref="AuditedAttribute" /> or an <c>IAuditDefinition&lt;TMessage&gt;</c>.
    ///     </para>
    ///     <para>
    ///         The application must register an <see cref="IAuditTrail" /> implementation; the
    ///         <c>litebus.audit.trail</c> diagnostic probe reports when it is missing. Registering an
    ///         <see cref="IAuditOutcomeMapper" /> is optional and lets a refusal raised as an exception be recorded as
    ///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />; a refusal from a guard
    ///         is already recorded as a denial without one.
    ///     </para>
    /// </remarks>
    public CommandModuleBuilder EnableAuditing()
    {
        AuditingEnabled = true;
        return Register<CommandAuditCompletionHandler>();
    }

    /// <summary>
    ///     Registers the LiteBus in-process idempotency handlers, so a command declaring an idempotency key is applied
    ///     at most once.
    /// </summary>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         Three handlers, all of which ignore a command that declares no
    ///         <c>IdempotencyDeclaration</c>, so one call covers the axis and only the declaring commands pay for it.
    ///         Two shortcuts claim the key, one for commands that produce a result and one for those that do not, and a
    ///         completion handler settles the claim: applied on success, released on anything else, so a transient
    ///         failure does not burn the key and turn the retry into a false repeat.
    ///     </para>
    ///     <para>
    ///         The application must register an <c>IIdempotencyStore</c>; the <c>litebus.idempotency.store</c>
    ///         diagnostic probe reports when it is missing. Make its claim atomic by letting the storage engine refuse
    ///         the duplicate, and write it through the same unit of work the handler uses, or a crash between the claim
    ///         and the change leaves a key claimed for work that never happened.
    ///     </para>
    ///     <para>
    ///         The shortcut stage runs after guards and validators, so an unauthorized or malformed command cannot
    ///         claim a key.
    ///     </para>
    /// </remarks>
    public CommandModuleBuilder EnableIdempotency()
    {
        IdempotencyEnabled = true;

        Register(typeof(IdempotentCommandShortcut<>));
        Register(typeof(IdempotentCommandShortcut<,>));

        return Register<IdempotencyCompletionHandler>();
    }

    /// <summary>
    ///     Registers a command type for the message registry.
    /// </summary>
    /// <typeparam name="T">The command or command handler type to register.</typeparam>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    public CommandModuleBuilder Register<T>()
    {
        return Register(typeof(T));
    }

    /// <summary>
    ///     Registers a command type for the message registry.
    /// </summary>
    /// <param name="type">The command or command handler type to register.</param>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    public CommandModuleBuilder Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!IsCommandConstruct(type))
        {
            throw new LiteBusNotSupportedException($"The given type '{type.Name}' is not a command construct and cannot be registered.");
        }

        _types.Add(type);
        return this;
    }

    /// <summary>
    ///     Registers all concrete command and command handler types from the specified assembly.
    /// </summary>
    /// <param name="assembly">The assembly from which to register command types.</param>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="assembly" /> is <see langword="null" />.</exception>
    public CommandModuleBuilder RegisterFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        _assemblies.Add(assembly);
        return this;
    }

    /// <summary>
    ///     Declares a child module owned by the command module.
    /// </summary>
    /// <param name="module">The child module to build alongside the command module.</param>
    /// <returns>The current <see cref="CommandModuleBuilder" /> instance for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     The seam feature packages compose through, so a capability layered on the command axis is registered inside
    ///     <c>AddCommands</c> rather than as a second top-level module the caller has to remember to add.
    /// </remarks>
    public CommandModuleBuilder RegisterChildModule(IModule module)
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

    /// <summary>
    ///     Applies deferred contract registrations to the live registry.
    /// </summary>
    /// <param name="registry">The shared message contract registry.</param>
    public void ApplyContracts(IMessageContractRegistry registry)
    {
        _contracts.ApplyTo(registry);
    }

    /// <summary>
    ///     Applies deferred type and assembly registrations to the live message registry.
    /// </summary>
    /// <param name="messageRegistry">The message registry the command module builds against.</param>
    /// <remarks>
    ///     Explicit registrations are applied before assembly scans, matching the order in which the configuration
    ///     action expressed them: a type named directly is registered as declared, and a scan then fills in the rest.
    /// </remarks>
    internal void ApplyRegistrations(IMessageRegistry messageRegistry)
    {
        foreach (var type in _types)
        {
            messageRegistry.Register(type);
        }

        foreach (var assembly in _assemblies)
        {
            foreach (var registrableCommandConstruct in AssemblyTypeScanner.GetLoadableTypes(assembly)
                         .Where(static type => type is { IsClass: true, IsAbstract: false } && IsCommandConstruct(type)))
            {
                messageRegistry.RegisterFromScan(registrableCommandConstruct);
            }
        }
    }

    /// <summary>
    ///     Determines whether a type is a command message or implements a command handler contract.
    /// </summary>
    /// <param name="type">The candidate type.</param>
    /// <returns><see langword="true" /> when the type belongs to the command axis; otherwise, <see langword="false" />.</returns>
    internal static bool IsCommandConstruct(Type type)
    {
        if (typeof(ICommand).IsAssignableFrom(type))
        {
            return true;
        }

        return type.GetInterfaces().Any(static contract =>
        {
            if (!contract.IsGenericType)
            {
                return HandlerContracts.Contains(contract);
            }

            var contractDefinition = contract.GetGenericTypeDefinition();

            // A message definition declares metadata for a command rather than implementing a handler contract.
            // Both shapes count: the keyed one that types a single declaration, and the describe one that declares
            // several without an explicit interface implementation per value.
            if (contractDefinition == typeof(IMessageDefinition<,>) ||
                contractDefinition == typeof(IMessageDefinition<>))
            {
                return typeof(ICommand).IsAssignableFrom(contract.GetGenericArguments()[0]);
            }

            if (HandlerContracts.Contains(contractDefinition))
            {
                return true;
            }

            // A pipeline handler written against the messaging-level contract counts when its message type is
            // constrained to this axis. Without this, a cross-cutting guard has to be written once per axis, and the
            // code being copied is usually the code least safe to have two copies of.
            return MessagingHandlerContracts.NamesMessageAssignableTo(contract, typeof(ICommand));
        });
    }
}
