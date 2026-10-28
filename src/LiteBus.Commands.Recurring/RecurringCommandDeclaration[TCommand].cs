using System;

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
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    public RecurringCommandDeclaration(Func<TCommand> commandFactory, RecurringCommandHostOptions hostOptions)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(hostOptions);

        CommandFactory = commandFactory;
        HostOptions = hostOptions;
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
}
