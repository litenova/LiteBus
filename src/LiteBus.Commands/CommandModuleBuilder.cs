using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
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
        typeof(ICommandGuard<,>),
        typeof(ICommandShortcut<>),
        typeof(ICommandShortcut<,>),
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
    ///     Gets the message registry to which command types are registered.
    /// </summary>
    private readonly IMessageRegistry _messageRegistry;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandModuleBuilder" /> class.
    /// </summary>
    /// <param name="messageRegistry">The message registry to which commands will be registered.</param>
    /// <param name="contracts">The contract writer used during module configuration.</param>
    public CommandModuleBuilder(IMessageRegistry messageRegistry, IContractWriter contracts)
    {
        _messageRegistry = messageRegistry;
        ArgumentNullException.ThrowIfNull(contracts);
        Contracts = contracts;
    }

    /// <summary>
    ///     Gets the message contract writer for persisted command contracts.
    /// </summary>
    public IContractWriter Contracts { get; }

    /// <summary>
    ///     Gets a value indicating whether <see cref="EnableAuditing" /> was called.
    /// </summary>
    /// <remarks>
    ///     The module reads this after the configuration action runs, so it can register the diagnostic probe that reports
    ///     a missing <see cref="IAuditTrail" /> before the first audited mediation fails inside the completion stage.
    /// </remarks>
    internal bool AuditingEnabled { get; private set; }

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
    ///         <see cref="AuditOutcome.Denied" /> rather than <see cref="AuditOutcome.Failed" />; a refusal from a gate
    ///         is already recorded as a denial without one.
    ///     </para>
    /// </remarks>
    public CommandModuleBuilder EnableAuditing()
    {
        AuditingEnabled = true;
        return Register<CommandAuditCompletionHandler>();
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

        _messageRegistry.Register(type);
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

        foreach (var registrableCommandConstruct in assembly.GetTypes()
                     .Where(static type => type is { IsClass: true, IsAbstract: false } && IsCommandConstruct(type)))
        {
            _messageRegistry.Register(registrableCommandConstruct);
        }

        return this;
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
            if (contractDefinition == typeof(IMessageDefinition<,>))
            {
                return typeof(ICommand).IsAssignableFrom(contract.GetGenericArguments()[0]);
            }

            return HandlerContracts.Contains(contractDefinition);
        });
    }
}
