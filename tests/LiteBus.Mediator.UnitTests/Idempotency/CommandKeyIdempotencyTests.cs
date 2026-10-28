using DurableIdempotency = LiteBus.Messaging.Abstractions.DurableMessaging.Idempotency;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies a durable envelope can be keyed by the idempotency key the in-flight command was granted.
/// </summary>
[Collection("Sequential")]
public sealed class CommandKeyIdempotencyTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider with idempotency enabled and the in-memory store registered.
    /// </summary>
    /// <param name="capture">The capture shared with the probe handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CommandKeyCapture capture)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(new InMemoryIdempotencyStore());
        services.AddSingleton(capture);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<CommandKeyProbeCommand>();
                    builder.Register<CommandKeyProbeCommandHandler>();
                    builder.Register<CommandKeyProbeCommandDefinition>();
                    builder.Register<UnkeyedProbeCommand>();
                    builder.Register<UnkeyedProbeCommandHandler>();
                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Inside_a_keyed_command_FromCommandKey_returns_the_claimed_key()
    {
        var capture = new CommandKeyCapture();
        var provider = BuildProvider(capture);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CommandKeyProbeCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        capture.Plain.Should().Be(new DurableIdempotency.Keyed($"{typeof(CommandKeyProbeCommand).FullName}:p-1"));
        capture.Suffixed.Should().Be(new DurableIdempotency.Keyed($"{typeof(CommandKeyProbeCommand).FullName}:p-1:paid"));
    }

    [Fact]
    public async Task Inside_a_keyed_command_FromCommandKey_honors_the_conflict_mode()
    {
        var capture = new CommandKeyCapture();
        var provider = BuildProvider(capture);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new CommandKeyProbeCommand { PaymentId = "p-1" }).ConfigureAwait(false);

        capture.Strict.Should().Be(new DurableIdempotency.Keyed(
            $"{typeof(CommandKeyProbeCommand).FullName}:p-1:paid",
            IdempotencyConflictMode.Strict));
    }

    [Fact]
    public async Task Inside_an_undeclared_command_FromCommandKey_throws()
    {
        var provider = BuildProvider(new CommandKeyCapture());
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var act = async () => await mediator.SendAsync(new UnkeyedProbeCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no claimed idempotency key*").ConfigureAwait(false);
    }

    [Fact]
    public void FromCommandKey_outside_any_mediation_names_the_missing_scope()
    {
        AmbientExecutionContext.ResetForTesting();

        var act = () =>
        {
            DurableIdempotency.FromCommandKey();
        };

        act.Should().Throw<InvalidOperationException>().WithMessage("*ambient execution context*");
    }

    [Fact]
    public void FromCommandKey_rejects_a_blank_suffix()
    {
        AmbientExecutionContext.ResetForTesting();

        var act = () =>
        {
            DurableIdempotency.FromCommandKey("  ");
        };

        act.Should().Throw<ArgumentException>().WithMessage("*suffix*");
    }
}

/// <summary>
///     Captures the keyed idempotency values the probe handler built from the granted command key.
/// </summary>
internal sealed class CommandKeyCapture
{
    /// <summary>
    ///     Gets or sets the unsuffixed command key.
    /// </summary>
    public DurableIdempotency.Keyed? Plain { get; set; }

    /// <summary>
    ///     Gets or sets the command key suffixed for one of several envelopes.
    /// </summary>
    public DurableIdempotency.Keyed? Suffixed { get; set; }

    /// <summary>
    ///     Gets or sets the suffixed command key with strict conflict handling.
    /// </summary>
    public DurableIdempotency.Keyed? Strict { get; set; }
}

/// <summary>
///     A command declaring an idempotency key, whose handler keys durable envelopes by the granted claim.
/// </summary>
internal sealed class CommandKeyProbeCommand : ICommand
{
    /// <summary>
    ///     Gets or sets the payment identifier the key is projected from.
    /// </summary>
    public string PaymentId { get; set; } = string.Empty;
}

/// <summary>
///     Declares how a repeat of <see cref="CommandKeyProbeCommand" /> is recognised.
/// </summary>
internal sealed class CommandKeyProbeCommandDefinition : IIdempotencyDefinition<CommandKeyProbeCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<CommandKeyProbeCommand>(command => command.PaymentId);
}

/// <summary>
///     Keys durable envelopes by the claim the shortcut granted this mediation.
/// </summary>
internal sealed class CommandKeyProbeCommandHandler : ICommandHandler<CommandKeyProbeCommand>
{
    /// <summary>
    ///     The capture shared with the test.
    /// </summary>
    private readonly CommandKeyCapture _capture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandKeyProbeCommandHandler" /> class.
    /// </summary>
    /// <param name="capture">The capture shared with the test.</param>
    public CommandKeyProbeCommandHandler(CommandKeyCapture capture)
    {
        _capture = capture;
    }

    /// <inheritdoc />
    public Task HandleAsync(CommandKeyProbeCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _capture.Plain = DurableIdempotency.FromCommandKey();
        _capture.Suffixed = DurableIdempotency.FromCommandKey("paid");
        _capture.Strict = DurableIdempotency.FromCommandKey("paid", IdempotencyConflictMode.Strict);

        return Task.CompletedTask;
    }
}

/// <summary>
///     A command that declares no idempotency key, so no claim exists to build from.
/// </summary>
internal sealed class UnkeyedProbeCommand : ICommand;

/// <summary>
///     Reaches for a command key the mediation never claimed.
/// </summary>
internal sealed class UnkeyedProbeCommandHandler : ICommandHandler<UnkeyedProbeCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(UnkeyedProbeCommand message, CancellationToken cancellationToken = default)
    {
        _ = DurableIdempotency.FromCommandKey();

        return Task.CompletedTask;
    }
}
