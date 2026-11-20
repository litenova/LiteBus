using System.Collections.Concurrent;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Storage.Marten;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends an idempotent command through the Marten idempotency store: a unit of work whose commit throws releases
///     the key, a retry runs the handler again, and a repeat after completion is answered without running it.
/// </summary>
internal static class IdempotencySettlementSmoke
{
    /// <summary>
    ///     The idempotency scope shared by the settlement commands.
    /// </summary>
    internal const string SettlementScope = "smoke-settlement";

    /// <summary>
    ///     Runs the idempotency settlement scenario.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync(string connectionString)
    {
        var documentStore = DocumentStore.For(options =>
        {
            options.Connection(connectionString);
            options.DatabaseSchemaName = "litebus_smoke_marten";
        });

        await using (documentStore.ConfigureAwait(false))
        {
            var counter = new SmokeSettlementCounter();
            var gate = new SmokeCommitGate();
            var services = new ServiceCollection();
            services.AddSingleton(counter);
            services.AddSingleton(gate);
            services.AddSingleton<IDocumentStore>(documentStore);
            services.AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ =>
                {
                });
                liteBus.AddCommands(commands =>
                {
                    commands.Register<SmokeSettleCommand>();
                    commands.Register<SmokeSettleCommandHandler>();
                    commands.Register<SmokeSettleCommandDefinition>();
                    commands.EnableIdempotency();
                    commands.UseMartenIdempotencyStore();
                    commands.UseUnitOfWork<SmokeFlakyCommitUnitOfWork>();
                });
            });

            await using var provider = services.BuildServiceProvider(Smoke.DevelopmentValidation);
            var mediator = provider.GetRequiredService<ICommandMediator>();

            var command = new SmokeSettleCommand(Guid.NewGuid());

            var threw = false;

            try
            {
                await mediator.SendAsync(command).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Smoke.Expect(threw, "the first send throws the commit failure");
            Smoke.Expect(counter.CountFor(command.CommandId) == 1, "the handler ran before the commit failed");

            await mediator.SendAsync(command).ConfigureAwait(false);
            Smoke.Expect(counter.CountFor(command.CommandId) == 2, "a retry after a failed commit runs the handler again");

            var repeat = await mediator.TrySendAsync(command).ConfigureAwait(false);
            Smoke.Expect(repeat.Outcome == MediationOutcome.Answered, "a repeat after completion is answered");
            Smoke.Expect(counter.CountFor(command.CommandId) == 2, "the answered repeat does not run the handler again");
        }
    }
}

/// <summary>
///     An idempotent command settled through the Marten idempotency store.
/// </summary>
/// <param name="CommandId">The command identifier, used as the idempotency key.</param>
internal sealed record SmokeSettleCommand(Guid CommandId) : ICommand;

/// <summary>
///     Records one handler run per command identifier.
/// </summary>
internal sealed class SmokeSettleCommandHandler : ICommandHandler<SmokeSettleCommand>
{
    /// <summary>
    ///     Counts how many times the handler actually ran.
    /// </summary>
    private readonly SmokeSettlementCounter _counter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeSettleCommandHandler" /> class.
    /// </summary>
    /// <param name="counter">Counts how many times the handler actually ran.</param>
    public SmokeSettleCommandHandler(SmokeSettlementCounter counter)
    {
        ArgumentNullException.ThrowIfNull(counter);
        _counter = counter;
    }

    /// <inheritdoc />
    public Task HandleAsync(SmokeSettleCommand message, CancellationToken cancellationToken = default)
    {
        _counter.Record(message.CommandId);
        return Task.CompletedTask;
    }
}

/// <summary>
///     Declares the settlement command idempotent on its command identifier.
/// </summary>
internal sealed class SmokeSettleCommandDefinition : IIdempotencyDefinition<SmokeSettleCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<SmokeSettleCommand>(command => command.CommandId.ToString("N"))
            with
        { Scope = IdempotencySettlementSmoke.SettlementScope };
}

/// <summary>
///     Counts how many times the settlement handler actually ran.
/// </summary>
internal sealed class SmokeSettlementCounter
{
    /// <summary>
    ///     The command identifiers recorded by handler runs.
    /// </summary>
    private readonly ConcurrentBag<Guid> _executions = [];

    /// <summary>
    ///     Records one handler run.
    /// </summary>
    /// <param name="commandId">The command identifier the handler ran for.</param>
    public void Record(Guid commandId)
    {
        _executions.Add(commandId);
    }

    /// <summary>
    ///     Counts the handler runs recorded for one command identifier.
    /// </summary>
    /// <param name="commandId">The command identifier to count.</param>
    /// <returns>The number of recorded runs.</returns>
    public int CountFor(Guid commandId)
    {
        return _executions.Count(executed => executed == commandId);
    }
}

/// <summary>
///     Fails the first unit-of-work commit and lets later commits through.
/// </summary>
internal sealed class SmokeCommitGate
{
    /// <summary>
    ///     The number of commits still to fail.
    /// </summary>
    private int _failuresRemaining = 1;

    /// <summary>
    ///     Decides whether the current commit fails.
    /// </summary>
    /// <returns><see langword="true" /> for the first commit; otherwise, <see langword="false" />.</returns>
    public bool ShouldFail()
    {
        return Interlocked.Decrement(ref _failuresRemaining) >= 0;
    }
}

/// <summary>
///     A unit of work whose first commit throws, so the settlement scenario proves the key is released.
/// </summary>
internal sealed class SmokeFlakyCommitUnitOfWork : IUnitOfWork
{
    /// <summary>
    ///     Fails the first commit and lets later commits through.
    /// </summary>
    private readonly SmokeCommitGate _gate;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SmokeFlakyCommitUnitOfWork" /> class.
    /// </summary>
    /// <param name="gate">Fails the first commit and lets later commits through.</param>
    public SmokeFlakyCommitUnitOfWork(SmokeCommitGate gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        _gate = gate;
    }

    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_gate.ShouldFail())
        {
            throw new InvalidOperationException("The smoke unit of work commit fails once.");
        }

        return Task.CompletedTask;
    }
}
