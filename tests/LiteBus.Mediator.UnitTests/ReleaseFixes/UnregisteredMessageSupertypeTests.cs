using System.Collections.Concurrent;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes.UnregisteredMessageSupertypes;

/// <summary>
///     Verifies that a message whose own type was never registered reaches the handlers of every supertype it has, and
///     reaches the same main handler a registered message would.
/// </summary>
/// <remarks>
///     Such a message used to borrow the descriptor of one registered supertype, which carried only that supertype's
///     handlers, so a pre-handler on its interface was skipped and an event implementing two handled interfaces threw
///     <see cref="AmbiguousMessageResolveException" />. It is now registered on its first dispatch, which links every
///     applicable handler, and both paths choose a main handler by the same most-derived rule.
/// </remarks>
[Collection("Sequential")]
public sealed class UnregisteredMessageSupertypeTests : LiteBusTestBase
{
    [Fact]
    public async Task An_unregistered_command_runs_its_interface_pre_handler_and_its_base_class_handler()
    {
        var recorder = new SupertypeRecorder();
        var provider = BuildProvider(recorder, commands: builder =>
        {
            builder.Register<OrderCommandHandler>();
            builder.Register<AuditedCommandPreHandler>();
        });

        await provider.GetRequiredService<ICommandMediator>().SendAsync(new PlaceOrderCommand()).ConfigureAwait(false);

        recorder.Entries.Should().Equal("audit-pre", "order-handled");
    }

    [Fact]
    public async Task An_unregistered_command_reaches_a_main_handler_registered_for_its_interface()
    {
        var recorder = new SupertypeRecorder();
        var provider = BuildProvider(recorder, commands: builder =>
        {
            builder.Register<OrderCommandPreHandler>();
            builder.Register<AuditedCommandHandler>();
        });

        await provider.GetRequiredService<ICommandMediator>().SendAsync(new PlaceOrderCommand()).ConfigureAwait(false);

        recorder.Entries.Should().Equal("order-pre", "audited-handled");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_most_derived_main_handler_wins_whether_or_not_the_command_was_registered(bool registered)
    {
        var recorder = new SupertypeRecorder();
        var provider = BuildProvider(recorder, commands: builder =>
        {
            builder.Register<OrderCommandHandler>();
            builder.Register<AuditedCommandHandler>();

            if (registered)
            {
                builder.Register<PlaceOrderCommand>();
            }
        });

        await provider.GetRequiredService<ICommandMediator>().SendAsync(new PlaceOrderCommand()).ConfigureAwait(false);

        recorder.Entries.Should().Equal("order-handled");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Equally_derived_main_handlers_are_ambiguous_whether_or_not_the_command_was_registered(bool registered)
    {
        var provider = BuildProvider(new SupertypeRecorder(), commands: builder =>
        {
            builder.Register<AuditedCommandHandler>();
            builder.Register<TenantCommandHandler>();

            if (registered)
            {
                builder.Register<AuditedTenantCommand>();
            }
        });

        var act = () => provider.GetRequiredService<ICommandMediator>().SendAsync(new AuditedTenantCommand());

        await act.Should().ThrowAsync<AmbiguousMessageResolveException>().ConfigureAwait(false);
    }

    [Fact]
    public async Task An_unregistered_event_runs_the_handlers_of_its_base_class_and_its_interface()
    {
        var recorder = new SupertypeRecorder();
        var provider = BuildProvider(recorder, events: builder =>
        {
            builder.Register<ShipmentEventHandler>();
            builder.Register<AuditedEventHandler>();
        });

        await provider.GetRequiredService<IEventMediator>().PublishAsync(new ParcelShippedEvent()).ConfigureAwait(false);

        recorder.Entries.Should().BeEquivalentTo(["shipment-event", "audited-event"]);
    }

    [Fact]
    public async Task An_unregistered_event_implementing_two_handled_interfaces_runs_both_handlers()
    {
        var recorder = new SupertypeRecorder();
        var provider = BuildProvider(recorder, events: builder =>
        {
            builder.Register<AuditedEventHandler>();
            builder.Register<TenantEventHandler>();
        });

        await provider.GetRequiredService<IEventMediator>().PublishAsync(new AuditedTenantEvent()).ConfigureAwait(false);

        recorder.Entries.Should().BeEquivalentTo(["audited-event", "tenant-event"]);
    }

    [Fact]
    public async Task Concurrent_first_sends_of_unregistered_types_run_every_handler_once()
    {
        var recorder = new SupertypeRecorder();
        var provider = BuildProvider(recorder, commands: builder =>
        {
            builder.Register(typeof(CountingCommandPreHandler<>));
            builder.Register(typeof(BatchCommandHandler<>));
        });
        var mediator = provider.GetRequiredService<ICommandMediator>();

        // Every closed type is registered by whichever send reaches it first while the others are dispatching.
        Func<Task>[] senders =
        [
            () => mediator.SendAsync(new BatchCommand<int>()),
            () => mediator.SendAsync(new BatchCommand<long>()),
            () => mediator.SendAsync(new BatchCommand<string>()),
            () => mediator.SendAsync(new BatchCommand<Guid>()),
            () => mediator.SendAsync(new BatchCommand<decimal>()),
            () => mediator.SendAsync(new BatchCommand<DateTime>()),
            () => mediator.SendAsync(new BatchCommand<bool>()),
            () => mediator.SendAsync(new BatchCommand<byte>())
        ];

        const int sendsPerType = 25;
        var sends = Enumerable.Range(0, sendsPerType)
            .SelectMany(_ => senders)
            .Select(send => Task.Run(send))
            .ToArray();

        await Task.WhenAll(sends).ConfigureAwait(false);

        recorder.Counts.Should().HaveCount(senders.Length * 2);
        recorder.Counts.Values.Should().OnlyContain(count => count == sendsPerType);
    }

    /// <summary>
    ///     Builds a provider with the messaging, command, and event modules.
    /// </summary>
    /// <param name="recorder">The recorder the handlers write to.</param>
    /// <param name="commands">Registers the command constructs under test.</param>
    /// <param name="events">Registers the event constructs under test.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        SupertypeRecorder recorder,
        Action<CommandModuleBuilder>? commands = null,
        Action<EventModuleBuilder>? events = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddCommands(builder => commands?.Invoke(builder));
                liteBus.AddEvents(builder => events?.Invoke(builder));
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Records what the handlers observed.
/// </summary>
internal sealed class SupertypeRecorder
{
    /// <summary>
    ///     Holds the recorded entries in the order they were written.
    /// </summary>
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>
    ///     Gets the recorded entries in the order they were written.
    /// </summary>
    public IReadOnlyList<string> Entries => [.. _entries];

    /// <summary>
    ///     Gets how many times each counted handler ran, keyed by handler and message.
    /// </summary>
    public ConcurrentDictionary<string, int> Counts { get; } = new();

    /// <summary>
    ///     Records one entry.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    public void Record(string entry)
    {
        _entries.Enqueue(entry);
    }

    /// <summary>
    ///     Counts one run of a handler for a message.
    /// </summary>
    /// <param name="key">The handler and message the run belongs to.</param>
    public void Count(string key)
    {
        Counts.AddOrUpdate(key, 1, static (_, count) => count + 1);
    }
}

/// <summary>
///     An interface whose pre-handler audits every command that implements it.
/// </summary>
internal interface IAuditedCommand : ICommand;

/// <summary>
///     A second command interface with a handler of its own.
/// </summary>
internal interface ITenantCommand : ICommand;

/// <summary>
///     A base command with its own handler.
/// </summary>
internal class OrderCommand : ICommand;

/// <summary>
///     A command that is never registered and inherits <see cref="OrderCommand" /> while implementing
///     <see cref="IAuditedCommand" />.
/// </summary>
internal sealed class PlaceOrderCommand : OrderCommand, IAuditedCommand;

/// <summary>
///     A command that is never registered and implements two command interfaces that each have a main handler.
/// </summary>
internal sealed class AuditedTenantCommand : IAuditedCommand, ITenantCommand;

/// <summary>
///     Handles <see cref="OrderCommand" /> and every command derived from it.
/// </summary>
internal sealed class OrderCommandHandler : ICommandHandler<OrderCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public OrderCommandHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(OrderCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("order-handled");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Runs before every command derived from <see cref="OrderCommand" />.
/// </summary>
internal sealed class OrderCommandPreHandler : ICommandPreHandler<OrderCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OrderCommandPreHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public OrderCommandPreHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(OrderCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("order-pre");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Audits every command that implements <see cref="IAuditedCommand" />.
/// </summary>
internal sealed class AuditedCommandPreHandler : ICommandPreHandler<IAuditedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditedCommandPreHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public AuditedCommandPreHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(IAuditedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("audit-pre");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles every command that implements <see cref="IAuditedCommand" />.
/// </summary>
internal sealed class AuditedCommandHandler : ICommandHandler<IAuditedCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditedCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public AuditedCommandHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(IAuditedCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("audited-handled");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles every command that implements <see cref="ITenantCommand" />.
/// </summary>
internal sealed class TenantCommandHandler : ICommandHandler<ITenantCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(ITenantCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     A generic command sent with many type arguments at once.
/// </summary>
/// <typeparam name="T">The batch item type.</typeparam>
internal sealed class BatchCommand<T> : ICommand;

/// <summary>
///     Counts each run for each closed <see cref="BatchCommand{T}" />.
/// </summary>
/// <typeparam name="T">The batch item type.</typeparam>
internal sealed class BatchCommandHandler<T> : ICommandHandler<BatchCommand<T>>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="BatchCommandHandler{T}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public BatchCommandHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(BatchCommand<T> message, CancellationToken cancellationToken = default)
    {
        _recorder.Count($"handled:{typeof(T).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Counts each run for each command it is closed over, through an open generic pre-handler.
/// </summary>
/// <typeparam name="TCommand">The command the registry closed this pre-handler over.</typeparam>
internal sealed class CountingCommandPreHandler<TCommand> : ICommandPreHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CountingCommandPreHandler{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public CountingCommandPreHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Count($"pre:{typeof(TCommand).GetGenericArguments().Single().Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An event interface with a handler of its own.
/// </summary>
internal interface IAuditedEvent : IEvent;

/// <summary>
///     A second event interface with a handler of its own.
/// </summary>
internal interface ITenantEvent : IEvent;

/// <summary>
///     A base event with its own handler.
/// </summary>
internal class ShipmentEvent : IEvent;

/// <summary>
///     An event that is never registered and inherits <see cref="ShipmentEvent" /> while implementing
///     <see cref="IAuditedEvent" />.
/// </summary>
internal sealed class ParcelShippedEvent : ShipmentEvent, IAuditedEvent;

/// <summary>
///     An event that is never registered and implements two handled event interfaces.
/// </summary>
internal sealed class AuditedTenantEvent : IAuditedEvent, ITenantEvent;

/// <summary>
///     Handles <see cref="ShipmentEvent" /> and every event derived from it.
/// </summary>
internal sealed class ShipmentEventHandler : IEventHandler<ShipmentEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ShipmentEventHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public ShipmentEventHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(ShipmentEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("shipment-event");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles every event that implements <see cref="IAuditedEvent" />.
/// </summary>
internal sealed class AuditedEventHandler : IEventHandler<IAuditedEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditedEventHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public AuditedEventHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(IAuditedEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("audited-event");
        return Task.CompletedTask;
    }
}

/// <summary>
///     Handles every event that implements <see cref="ITenantEvent" />.
/// </summary>
internal sealed class TenantEventHandler : IEventHandler<ITenantEvent>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SupertypeRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TenantEventHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public TenantEventHandler(SupertypeRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(ITenantEvent message, CancellationToken cancellationToken = default)
    {
        _recorder.Record("tenant-event");
        return Task.CompletedTask;
    }
}
