using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Commands.Recurring.Schedules.Cron;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends one command through a single recurring loop scheduled by a cron expression and verifies the loop ran it.
/// </summary>
internal static class RecurringCronSmoke
{
    /// <summary>
    ///     How long the scenario waits for the cron loop to send its command.
    /// </summary>
    private static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Runs the recurring cron scenario.
    /// </summary>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync()
    {
        var gate = new TaskCompletionSource();
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands =>
            {
                commands.Register<RecurringCronCommandHandler>();
                commands.AddRecurring(
                    () => new RecurringCronCommand(),
                    host => host.Schedule = CronSchedules.FromExpression("* * * * * *", CronExpressionFormat.IncludeSeconds));
            });
        });

        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetRequiredService<IHostedService>();
        await hosted.StartAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            var finished = await Task.WhenAny(gate.Task, Task.Delay(SendTimeout)).ConfigureAwait(false);
            Smoke.Expect(ReferenceEquals(finished, gate.Task), "the cron-scheduled loop sent its command");
        }
        finally
        {
            await hosted.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}

/// <summary>
///     A command the cron-scheduled loop sends.
/// </summary>
internal sealed record RecurringCronCommand : ICommand;

/// <summary>
///     Signals the scenario gate when the cron-scheduled loop sends its command.
/// </summary>
internal sealed class RecurringCronCommandHandler : ICommandHandler<RecurringCronCommand>
{
    /// <summary>
    ///     The gate completed when the command runs.
    /// </summary>
    private readonly TaskCompletionSource _gate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecurringCronCommandHandler" /> class.
    /// </summary>
    /// <param name="gate">The gate completed when the command runs.</param>
    public RecurringCronCommandHandler(TaskCompletionSource gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _gate = gate;
    }

    /// <inheritdoc />
    public Task HandleAsync(RecurringCronCommand message, CancellationToken cancellationToken = default)
    {
        _gate.TrySetResult();
        return Task.CompletedTask;
    }
}
