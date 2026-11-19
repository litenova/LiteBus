using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     The registered intent to send one command repeatedly for the life of the host.
/// </summary>
/// <typeparam name="TCommand">The command type sent on each pass.</typeparam>
/// <remarks>
///     Registered as a singleton so the background service for this command type resolves its own factory and options
///     without the two being matched up by position or name.
/// </remarks>
public sealed class RecurringCommandDeclaration<TCommand>
    where TCommand : notnull
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCommandDeclaration{TCommand}" /> class.
    /// </summary>
    /// <param name="commandFactory">Produces the command instance sent on each pass.</param>
    /// <param name="hostOptions">The lifecycle and timing settings for the loop.</param>
    /// <param name="identityOf">
    ///     Computes the identity one pass sends for from the command it built, or <see langword="null" /> to send
    ///     without an identity.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="commandFactory" /> or <paramref name="hostOptions" /> is <see langword="null" />.
    /// </exception>
    public RecurringCommandDeclaration(
        Func<TCommand> commandFactory,
        RecurringCommandHostOptions hostOptions,
        Func<TCommand, ExecutionIdentity>? identityOf = null)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(hostOptions);

        CommandFactory = commandFactory;
        HostOptions = hostOptions;
        IdentityOf = identityOf;
    }

    /// <summary>
    ///     Gets the factory that produces the command instance sent on each pass.
    /// </summary>
    /// <value>
    ///     Called once per pass rather than once at registration, so a command carrying a timestamp or a fresh
    ///     identifier gets a new one each time instead of the same value for the life of the process.
    /// </value>
    public Func<TCommand> CommandFactory { get; }

    /// <summary>
    ///     Gets the lifecycle and timing settings for the loop.
    /// </summary>
    public RecurringCommandHostOptions HostOptions { get; }

    /// <summary>
    ///     Gets the function that computes the identity one pass sends for from the command it built.
    /// </summary>
    /// <value>
    ///     The function, or <see langword="null" /> when passes send without an identity and the mediation resolves it
    ///     from the registered <see cref="IExecutionIdentityProvider" /> or falls back to
    ///     <see cref="ExecutionIdentity.Anonymous" />.
    /// </value>
    public Func<TCommand, ExecutionIdentity>? IdentityOf { get; }
}
