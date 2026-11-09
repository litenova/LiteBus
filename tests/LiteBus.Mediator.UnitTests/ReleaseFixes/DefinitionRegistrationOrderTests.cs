using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes.DefinitionRegistrationOrder;

/// <summary>
///     Verifies that a definition registering a message for the first time links that message to the handlers registered
///     before it, so the outcome does not depend on whether the definition or the handler was registered first.
/// </summary>
/// <remarks>
///     A message without a registration of its own, such as a derived command handled by its base class or a closed
///     generic command handled by its open definition, is registered by the first definition that describes it. That
///     message used to commit with no handlers when the handler had been registered first, and sending it threw
///     <see cref="NoHandlerFoundException" />.
/// </remarks>
[Collection("Sequential")]
public sealed class DefinitionRegistrationOrderTests : LiteBusTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_definition_for_a_derived_command_keeps_the_base_class_handler(bool handlerFirst)
    {
        var recorder = new DefinitionOrderRecorder();
        var provider = BuildProvider(recorder, builder =>
        {
            if (handlerFirst)
            {
                builder.Register<BaseTransferCommandHandler>();
                builder.Register<DerivedTransferCommandDefinition>();
            }
            else
            {
                builder.Register<DerivedTransferCommandDefinition>();
                builder.Register<BaseTransferCommandHandler>();
            }
        });

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DerivedTransferCommand { TransferId = "t-1" })
            .ConfigureAwait(false);

        recorder.Entries.Should().Equal("base-handled:t-1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_definition_for_a_closed_generic_command_keeps_the_open_handler(bool handlerFirst)
    {
        var recorder = new DefinitionOrderRecorder();
        var provider = BuildProvider(recorder, builder =>
        {
            if (handlerFirst)
            {
                builder.Register(typeof(GenericTransferCommandHandler<>));
                builder.Register<GenericTransferCommandDefinition>();
            }
            else
            {
                builder.Register<GenericTransferCommandDefinition>();
                builder.Register(typeof(GenericTransferCommandHandler<>));
            }
        });

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new GenericTransferCommand<int> { TransferId = "t-2" })
            .ConfigureAwait(false);

        recorder.Entries.Should().Equal("generic-handled:Int32:t-2");
    }

    [Fact]
    public async Task An_open_generic_guard_registered_first_reaches_a_message_its_definition_registered()
    {
        var recorder = new DefinitionOrderRecorder();
        var provider = BuildProvider(recorder, builder =>
        {
            builder.Register(typeof(DenyingTransferGuard<>));
            builder.Register<BaseTransferCommandHandler>();
            builder.Register<DerivedTransferCommandDefinition>();
        });

        var act = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DerivedTransferCommand { TransferId = "t-3" });

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        recorder.Entries.Should().BeEmpty();
    }

    /// <summary>
    ///     Builds a provider with the messaging and command modules.
    /// </summary>
    /// <param name="recorder">The recorder the handlers write to.</param>
    /// <param name="commands">Registers the command constructs under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(DefinitionOrderRecorder recorder, Action<CommandModuleBuilder> commands)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddCommands(commands);
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Records what the handlers observed, in order.
/// </summary>
internal sealed class DefinitionOrderRecorder
{
    /// <summary>
    ///     Holds the recorded entries.
    /// </summary>
    private readonly List<string> _entries = [];

    /// <summary>
    ///     Gets the recorded entries in the order they were written.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>
    ///     Records one entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    public void Record(string entry)
    {
        _entries.Add(entry);
    }
}

/// <summary>
///     A command whose handler covers every command derived from it.
/// </summary>
internal class BaseTransferCommand : ICommand
{
    /// <summary>
    ///     Gets the transfer identifier, which the definitions use as the idempotency key.
    /// </summary>
    public required string TransferId { get; init; }
}

/// <summary>
///     A derived command with no handler of its own, registered only by its definition.
/// </summary>
internal sealed class DerivedTransferCommand : BaseTransferCommand;

/// <summary>
///     Declares an idempotency key for <see cref="DerivedTransferCommand" />.
/// </summary>
internal sealed class DerivedTransferCommandDefinition : IIdempotencyDefinition<DerivedTransferCommand>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<DerivedTransferCommand>(command => command.TransferId);
}

/// <summary>
///     Handles <see cref="BaseTransferCommand" /> and every command derived from it.
/// </summary>
internal sealed class BaseTransferCommandHandler : ICommandHandler<BaseTransferCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DefinitionOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BaseTransferCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public BaseTransferCommandHandler(DefinitionOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(BaseTransferCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"base-handled:{message.TransferId}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A generic command handled by its open generic handler.
/// </summary>
/// <typeparam name="TAmount">The amount type.</typeparam>
internal sealed class GenericTransferCommand<TAmount> : ICommand
{
    /// <summary>
    ///     Gets the transfer identifier, which the definition uses as the idempotency key.
    /// </summary>
    public required string TransferId { get; init; }
}

/// <summary>
///     Declares an idempotency key for the closed command <see cref="GenericTransferCommand{TAmount}" /> of
///     <see cref="int" />, the only way to declare metadata for a generic message.
/// </summary>
internal sealed class GenericTransferCommandDefinition : IIdempotencyDefinition<GenericTransferCommand<int>>
{
    /// <inheritdoc />
    public IdempotencyDeclaration Idempotency =>
        IdempotencyDeclaration.KeyedBy<GenericTransferCommand<int>>(command => command.TransferId);
}

/// <summary>
///     Handles every closed <see cref="GenericTransferCommand{TAmount}" />.
/// </summary>
/// <typeparam name="TAmount">The amount type.</typeparam>
internal sealed class GenericTransferCommandHandler<TAmount> : ICommandHandler<GenericTransferCommand<TAmount>>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DefinitionOrderRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GenericTransferCommandHandler{TAmount}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public GenericTransferCommandHandler(DefinitionOrderRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(GenericTransferCommand<TAmount> message, CancellationToken cancellationToken = default)
    {
        _recorder.Record($"generic-handled:{typeof(TAmount).Name}:{message.TransferId}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Denies every transfer command, through an open generic guard closed per message.
/// </summary>
/// <typeparam name="TCommand">The command the registry closed this guard over.</typeparam>
internal sealed class DenyingTransferGuard<TCommand> : ICommandGuard<TCommand>
    where TCommand : BaseTransferCommand
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Verdict.Deny("transfers are closed"));
    }
}
