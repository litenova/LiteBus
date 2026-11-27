using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies an <see cref="IIdempotencyDefinition{TMessage}" /> can declare a command exempt through
///     <see cref="IdempotencyDeclaration.Exempt" />, with the same effect as
///     <c>declarations.Exempt&lt;IdempotencyDeclaration&gt;(rationale)</c>.
/// </summary>
[Collection("Sequential")]
public sealed class IdempotencyExemptDefinitionTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider that requires an idempotency declaration of every command and enables idempotency.
    /// </summary>
    /// <param name="store">The idempotency store the shortcut claims keys in.</param>
    /// <param name="commandTypes">The command, handler, and definition types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(InMemoryIdempotencyStore store, params Type[] commandTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        services.AddSingleton(new ExemptRunCounter());

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging => messaging.RequireDeclaration<IdempotencyDeclaration, ICommand>());

                registry.AddCommands(builder =>
                {
                    foreach (var commandType in commandTypes)
                    {
                        builder.Register(commandType);
                    }

                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public void Exempt_rejects_a_blank_rationale()
    {
        var act = () => IdempotencyDeclaration.Exempt("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Exempt_records_the_rationale_and_requires_no_idempotency()
    {
        var declaration = IdempotencyDeclaration.Exempt("Renaming to the same name twice leaves the same state.");

        declaration.Rationale.Should().Be("Renaming to the same name twice leaves the same state.");
        declaration.RequiresIdempotency.Should().BeFalse();
        IdempotencyDeclaration.KeyedByContext().RequiresIdempotency.Should().BeTrue();
    }

    [Fact]
    public void An_exempt_definition_satisfies_the_required_declaration_at_composition()
    {
        var provider = BuildProvider(
            new InMemoryIdempotencyStore(),
            typeof(RenameLedgerCommand),
            typeof(RenameLedgerCommandHandler),
            typeof(RenameLedgerCommandDefinition));

        var accessor = provider.GetRequiredService<IMessageMetadataAccessor>();

        accessor.TryGet<RenameLedgerCommand, IdempotencyDeclaration>(out var declaration).Should().BeTrue();
        declaration.Should().BeOfType<IdempotencyExemptDeclaration>()
            .Which.Rationale.Should().Be("Renaming to the same name twice leaves the same state.");
    }

    [Fact]
    public void A_command_with_no_idempotency_declaration_still_fails_composition()
    {
        var act = () => BuildProvider(
            new InMemoryIdempotencyStore(),
            typeof(UndeclaredLedgerCommand),
            typeof(UndeclaredLedgerCommandHandler));

        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*IdempotencyDeclaration is required of every ICommand*UndeclaredLedgerCommand*");
    }

    [Fact]
    public async Task An_exempt_command_runs_every_time_and_claims_no_key()
    {
        var store = new InMemoryIdempotencyStore();
        var provider = BuildProvider(
            store,
            typeof(RenameLedgerCommand),
            typeof(RenameLedgerCommandHandler),
            typeof(RenameLedgerCommandDefinition));
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new RenameLedgerCommand { Name = "Ledger" }).ConfigureAwait(false);
        await mediator.SendAsync(new RenameLedgerCommand { Name = "Ledger" }).ConfigureAwait(false);

        provider.GetRequiredService<ExemptRunCounter>().Runs.Should().Be(2);
        store.AppliedKeys.Should().BeEmpty();
    }
}

/// <summary>
///     Counts how many times the exempt command's handler ran.
/// </summary>
internal sealed class ExemptRunCounter
{
    /// <summary>
    ///     Gets or sets the number of handler runs.
    /// </summary>
    public int Runs { get; set; }
}

/// <summary>
///     A command whose repeat is harmless, so it is declared exempt from idempotency.
/// </summary>
internal sealed class RenameLedgerCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the new ledger name.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>
///     Declares <see cref="RenameLedgerCommand" /> exempt from idempotency through the typed definition interface.
/// </summary>
internal sealed class RenameLedgerCommandDefinition : IIdempotencyDefinition<RenameLedgerCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.Exempt("Renaming to the same name twice leaves the same state.");
}

/// <summary>
///     Counts each run of <see cref="RenameLedgerCommand" />.
/// </summary>
internal sealed class RenameLedgerCommandHandler : ICommandHandler<RenameLedgerCommand>
{
    /// <summary>
    ///     The counter shared with the test.
    /// </summary>
    private readonly ExemptRunCounter _counter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RenameLedgerCommandHandler" /> class.
    /// </summary>
    /// <param name="counter">The counter shared with the test.</param>
    public RenameLedgerCommandHandler(ExemptRunCounter counter)
    {
        _counter = counter;
    }

    /// <inheritdoc />
    public Task HandleAsync(RenameLedgerCommand message, CancellationToken cancellationToken = default)
    {
        _counter.Runs++;

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that declares no idempotency position at all.
/// </summary>
internal sealed class UndeclaredLedgerCommand : ICommand;

/// <summary>
///     Handles <see cref="UndeclaredLedgerCommand" />.
/// </summary>
internal sealed class UndeclaredLedgerCommandHandler : ICommandHandler<UndeclaredLedgerCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UndeclaredLedgerCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
