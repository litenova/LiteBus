using System;

namespace LiteBus.Commands.Recurring;

/// <summary>
///     The registered intent to send one command per tenant repeatedly for the life of the host.
/// </summary>
/// <typeparam name="TCommand">The command type sent once per tenant on each pass.</typeparam>
/// <remarks>
///     Registered as a singleton so the background service for this command type resolves its own factory, tenant
///     source, and options without the three being matched up by position or name.
/// </remarks>
public sealed class RecurringTenantSweepDeclaration<TCommand>
    where TCommand : notnull
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringTenantSweepDeclaration{TCommand}" /> class, with the
    ///     tenant source resolved from the dependency registry.
    /// </summary>
    /// <param name="commandFactory">Builds the command sent for one tenant.</param>
    /// <param name="hostOptions">The lifecycle and timing settings for the loop.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="commandFactory" /> or <paramref name="hostOptions" /> is <see langword="null" />.
    /// </exception>
    public RecurringTenantSweepDeclaration(Func<string, TCommand> commandFactory, RecurringCommandHostOptions hostOptions)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(hostOptions);

        CommandFactory = commandFactory;
        HostOptions = hostOptions;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringTenantSweepDeclaration{TCommand}" /> class, with a
    ///     tenant source instance.
    /// </summary>
    /// <param name="commandFactory">Builds the command sent for one tenant.</param>
    /// <param name="hostOptions">The lifecycle and timing settings for the loop.</param>
    /// <param name="tenantSource">The tenant source each pass enumerates.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    public RecurringTenantSweepDeclaration(
        Func<string, TCommand> commandFactory,
        RecurringCommandHostOptions hostOptions,
        IRecurringTenantSource tenantSource)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(tenantSource);

        CommandFactory = commandFactory;
        HostOptions = hostOptions;
        TenantSource = tenantSource;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringTenantSweepDeclaration{TCommand}" /> class, with a
    ///     tenant source type resolved from a dispatch scope.
    /// </summary>
    /// <param name="commandFactory">Builds the command sent for one tenant.</param>
    /// <param name="hostOptions">The lifecycle and timing settings for the loop.</param>
    /// <param name="tenantSourceType">The tenant source implementation each pass resolves.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    ///     <paramref name="tenantSourceType" /> does not implement <see cref="IRecurringTenantSource" />.
    /// </exception>
    public RecurringTenantSweepDeclaration(
        Func<string, TCommand> commandFactory,
        RecurringCommandHostOptions hostOptions,
        Type tenantSourceType)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(tenantSourceType);

        if (!typeof(IRecurringTenantSource).IsAssignableFrom(tenantSourceType))
        {
            throw new ArgumentException(
                $"The tenant source type '{tenantSourceType.FullName}' does not implement IRecurringTenantSource.",
                nameof(tenantSourceType));
        }

        CommandFactory = commandFactory;
        HostOptions = hostOptions;
        TenantSourceType = tenantSourceType;
    }

    /// <summary>
    ///     Gets the factory that builds the command sent for one tenant.
    /// </summary>
    /// <value>
    ///     Called once per tenant on every pass rather than once at registration, so a command carrying a timestamp or
    ///     a fresh identifier gets a new one each time instead of the same value for the life of the process.
    /// </value>
    public Func<string, TCommand> CommandFactory { get; }

    /// <summary>
    ///     Gets the lifecycle and timing settings for the loop.
    /// </summary>
    public RecurringCommandHostOptions HostOptions { get; }

    /// <summary>
    ///     Gets the tenant source each pass enumerates.
    /// </summary>
    /// <value>
    ///     The instance, or <see langword="null" /> when the loop resolves the source from a dispatch scope instead.
    /// </value>
    public IRecurringTenantSource? TenantSource { get; }

    /// <summary>
    ///     Gets the tenant source type each pass resolves from a dispatch scope.
    /// </summary>
    /// <value>
    ///     The implementation type, or <see langword="null" /> when <see cref="TenantSource" /> carries an instance or
    ///     the loop resolves <see cref="IRecurringTenantSource" /> itself from the dependency registry.
    /// </value>
    public Type? TenantSourceType { get; }
}
