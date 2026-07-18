using System.Collections.Concurrent;
using Autofac;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Autofac;
using LiteBus.Messaging;

namespace LiteBus.Runtime.UnitTests;

/// <summary>
///     Verifies that Autofac owns one lifetime scope for each message dispatch.
/// </summary>
public sealed class AutofacMessageDispatchScopeTests
{
    /// <summary>
    ///     Confirms sequential command dispatches resolve distinct scoped dependencies and dispose both scopes.
    /// </summary>
    /// <returns>A task that completes after both commands are dispatched.</returns>
    [Fact]
    public async Task SendAsync_Twice_ShouldCreateAndDisposeDistinctAutofacScopes()
    {
        var recorder = new ScopeRecorder();
        var builder = new ContainerBuilder();

        builder.RegisterInstance(recorder).SingleInstance();
        builder.RegisterType<ScopedDependency>().InstancePerLifetimeScope();
        builder.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });
            registry.AddCommands(commands =>
            {
                commands.Register<ScopedCommand>();
                commands.Register<ScopedCommandHandler>();
            });
        });

        using var container = builder.Build();
        var mediator = container.Resolve<ICommandMediator>();

        await mediator.SendAsync(new ScopedCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new ScopedCommand()).ConfigureAwait(false);

        recorder.UsedIds.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        recorder.DisposedIds.Should().BeEquivalentTo(recorder.UsedIds);
    }

    /// <summary>
    ///     Command used to exercise scoped handler resolution.
    /// </summary>
    private sealed record ScopedCommand : ICommand;

    /// <summary>
    ///     Records the scoped dependency resolved for a command dispatch.
    /// </summary>
    private sealed class ScopedCommandHandler : ICommandHandler<ScopedCommand>
    {
        /// <summary>
        ///     The dependency whose lifetime is under test.
        /// </summary>
        private readonly ScopedDependency _dependency;

        /// <summary>
        ///     Records dependency identifiers used by handlers.
        /// </summary>
        private readonly ScopeRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScopedCommandHandler" /> class.
        /// </summary>
        /// <param name="dependency">The scoped dependency.</param>
        /// <param name="recorder">The scope recorder.</param>
        public ScopedCommandHandler(ScopedDependency dependency, ScopeRecorder recorder)
        {
            _dependency = dependency;
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task HandleAsync(ScopedCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.RecordUse(_dependency.Id);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Scoped dependency that records when Autofac disposes it.
    /// </summary>
    private sealed class ScopedDependency : IDisposable
    {
        /// <summary>
        ///     Records disposal for the owning lifetime scope.
        /// </summary>
        private readonly ScopeRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ScopedDependency" /> class.
        /// </summary>
        /// <param name="recorder">The scope recorder.</param>
        public ScopedDependency(ScopeRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <summary>
        ///     Gets the unique dependency identifier.
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();

        /// <inheritdoc />
        public void Dispose()
        {
            _recorder.RecordDispose(Id);
        }
    }

    /// <summary>
    ///     Collects dependency use and disposal identifiers.
    /// </summary>
    private sealed class ScopeRecorder
    {
        /// <summary>
        ///     The disposed scoped dependency identifiers.
        /// </summary>
        private readonly ConcurrentBag<Guid> _disposedIds = [];

        /// <summary>
        ///     The scoped dependency identifiers used by handlers.
        /// </summary>
        private readonly ConcurrentBag<Guid> _usedIds = [];

        /// <summary>
        ///     Gets the disposed dependency identifiers.
        /// </summary>
        public IReadOnlyCollection<Guid> DisposedIds => _disposedIds;

        /// <summary>
        ///     Gets the used dependency identifiers.
        /// </summary>
        public IReadOnlyCollection<Guid> UsedIds => _usedIds;

        /// <summary>
        ///     Records a disposed dependency.
        /// </summary>
        /// <param name="id">The dependency identifier.</param>
        public void RecordDispose(Guid id)
        {
            _disposedIds.Add(id);
        }

        /// <summary>
        ///     Records a dependency used by a handler.
        /// </summary>
        /// <param name="id">The dependency identifier.</param>
        public void RecordUse(Guid id)
        {
            _usedIds.Add(id);
        }
    }
}
