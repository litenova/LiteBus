using System.Collections.Concurrent;
using Autofac;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Autofac;
using LiteBus.Messaging;

namespace LiteBus.Runtime.UnitTests;

/// <summary>
///     Verifies that Autofac resolves an open generic handler closed over a message type first registered at dispatch,
///     after the container was built.
/// </summary>
/// <remarks>
///     A closed generic message is registered on its first dispatch, which closes every applicable open generic pipeline
///     handler over it. Those closures did not exist when the container was built, so they resolve from the open generic
///     registration the module adds for each open handler definition.
/// </remarks>
public sealed class AutofacDispatchTimeClosureTests
{
    /// <summary>
    ///     Confirms an open generic pre-handler runs for each closed generic command through Autofac.
    /// </summary>
    /// <returns>A task that completes after both commands are dispatched.</returns>
    [Fact]
    public async Task An_open_generic_pre_handler_runs_for_closed_generic_commands_through_autofac()
    {
        var recorder = new ClosureRecorder();
        var builder = new ContainerBuilder();

        builder.RegisterInstance(recorder).SingleInstance();
        builder.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.AddCommands(commands =>
            {
                commands.Register(typeof(ClosureMarkedPreHandler<>));
                commands.Register(typeof(ClosureArchiveCommandHandler<>));
            });
        });

        using var container = builder.Build();
        var mediator = container.Resolve<ICommandMediator>();

        await mediator.SendAsync(new ClosureArchiveCommand<string>()).ConfigureAwait(false);
        await mediator.SendAsync(new ClosureArchiveCommand<int>()).ConfigureAwait(false);

        recorder.Entries.Should().Equal("pre:String", "archived:String", "pre:Int32", "archived:Int32");
    }

    /// <summary>
    ///     Records what the handlers observed, in order.
    /// </summary>
    private sealed class ClosureRecorder
    {
        /// <summary>
        ///     Gets the recorded entries in the order they were written.
        /// </summary>
        public ConcurrentQueue<string> Entries { get; } = new();
    }

    /// <summary>
    ///     Marks the commands in this test, so the open generic pre-handler stays out of compositions that scan this
    ///     assembly for other tests.
    /// </summary>
    private interface IClosureMarkedCommand : ICommand;

    /// <summary>
    ///     A generic command handled by its open generic handler.
    /// </summary>
    /// <typeparam name="T">The archived item type.</typeparam>
    private sealed class ClosureArchiveCommand<T> : IClosureMarkedCommand;

    /// <summary>
    ///     Handles every closed <see cref="ClosureArchiveCommand{T}" />.
    /// </summary>
    /// <typeparam name="T">The archived item type.</typeparam>
    private sealed class ClosureArchiveCommandHandler<T> : ICommandHandler<ClosureArchiveCommand<T>>
    {
        /// <summary>
        ///     The recorder shared with the test.
        /// </summary>
        private readonly ClosureRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ClosureArchiveCommandHandler{T}" /> class.
        /// </summary>
        /// <param name="recorder">The recorder shared with the test.</param>
        public ClosureArchiveCommandHandler(ClosureRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task HandleAsync(ClosureArchiveCommand<T> message, CancellationToken cancellationToken = default)
        {
            _recorder.Entries.Enqueue($"archived:{typeof(T).Name}");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Records the item type of each marked command it is closed over.
    /// </summary>
    /// <typeparam name="TCommand">The command the registry closed this pre-handler over.</typeparam>
    private sealed class ClosureMarkedPreHandler<TCommand> : ICommandPreHandler<TCommand>
        where TCommand : IClosureMarkedCommand
    {
        /// <summary>
        ///     The recorder shared with the test.
        /// </summary>
        private readonly ClosureRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ClosureMarkedPreHandler{TCommand}" /> class.
        /// </summary>
        /// <param name="recorder">The recorder shared with the test.</param>
        public ClosureMarkedPreHandler(ClosureRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Entries.Enqueue($"pre:{typeof(TCommand).GetGenericArguments().Single().Name}");
            return Task.CompletedTask;
        }
    }
}
