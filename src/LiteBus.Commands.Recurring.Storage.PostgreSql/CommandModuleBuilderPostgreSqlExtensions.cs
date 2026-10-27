using System;
using LiteBus.Commands;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Provides PostgreSQL single-flight lease extensions for <see cref="CommandModuleBuilder" />.
/// </summary>
public static class CommandModuleBuilderPostgreSqlExtensions
{
    /// <summary>
    ///     Registers the renewable PostgreSQL lease row used by recurring commands that require single-flight.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <param name="configure">The PostgreSQL recurring lease configuration callback.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="builder" /> or <paramref name="configure" /> is <see langword="null" />.
    /// </exception>
    /// <remarks>
    ///     Each acquisition takes one row in <c>litebus_recurring_leases</c> (configurable through
    ///     <see cref="PostgreSqlRecurringLeaseOptions" />) and renews it in the background while the pass holds it. A
    ///     holder whose process dies stops renewing, so its row becomes takeable once the lease duration lapses on the
    ///     database clock. Each statement opens its own pooled connection and no connection is held between statements,
    ///     so the lease works behind PgBouncer in transaction mode. Loops opt in per registration with
    ///     <see cref="RecurringCommandHostOptions.RequireLease" />.
    /// </remarks>
    /// <example>
    ///     <code><![CDATA[
    /// registry.AddCommands(commands => commands
    ///     .RegisterFromAssembly(typeof(SendDueRemindersCommand).Assembly)
    ///     .UsePostgreSqlRecurringLease(postgres =>
    ///     {
    ///         postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!);
    ///         postgres.UseOptions(new PostgreSqlRecurringLeaseOptions
    ///         {
    ///             LeaseDuration = TimeSpan.FromSeconds(30)
    ///         });
    ///     })
    ///     .AddRecurring(
    ///         () => new SendDueRemindersCommand { BatchSize = 50 },
    ///         host => host.RequireLease = true));
    /// ]]></code>
    /// </example>
    public static CommandModuleBuilder UsePostgreSqlRecurringLease(
        this CommandModuleBuilder builder,
        Action<PostgreSqlRecurringLeaseModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        return builder.RegisterChildModule(new PostgreSqlRecurringLeaseModule(configure));
    }
}
