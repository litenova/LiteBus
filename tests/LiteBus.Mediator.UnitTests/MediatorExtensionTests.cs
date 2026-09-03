using System.Runtime.CompilerServices;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Queries.Abstractions;

namespace LiteBus.Mediator.UnitTests;

/// <summary>
///     Verifies mediator convenience extensions preserve routing tags and cancellation tokens.
/// </summary>
public sealed class MediatorExtensionTests
{
    /// <summary>
    ///     Verifies every command extension forwards default or tagged settings to the mediator contract.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task CommandExtensions_ShouldForwardDefaultAndTaggedSettings()
    {
        var mediator = new RecordingCommandMediator();
        using var cancellation = new CancellationTokenSource();

        await mediator.SendAsync((ICommand)new TestCommand(), cancellation.Token).ConfigureAwait(false);
        var result = await mediator.SendAsync(new TestResultCommand(), cancellation.Token).ConfigureAwait(false);
        await mediator.SendAsync((ICommand)new TestCommand(), "command-tag", cancellation.Token).ConfigureAwait(false);
        var taggedResult = await mediator.SendAsync(new TestResultCommand(), "result-tag", cancellation.Token).ConfigureAwait(false);

        result.Should().Be(42);
        taggedResult.Should().Be(42);
        mediator.Settings.Should().HaveCount(4);
        mediator.Settings[0].Should().BeNull();
        mediator.Settings[1].Should().BeNull();
        mediator.Settings[2]!.Routing.Tags.Should().Equal("command-tag");
        mediator.Settings[3]!.Routing.Tags.Should().Equal("result-tag");
        mediator.Tokens.Should().OnlyContain(token => token == cancellation.Token);
    }

    /// <summary>
    ///     Verifies query and stream extensions forward default or tagged settings to the mediator contract.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task QueryExtensions_ShouldForwardDefaultAndTaggedSettings()
    {
        var mediator = new RecordingQueryMediator();
        using var cancellation = new CancellationTokenSource();

        var result = await mediator.QueryAsync(new TestQuery(), cancellation.Token).ConfigureAwait(false);
        var taggedResult = await mediator.QueryAsync(new TestQuery(), "query-tag", cancellation.Token).ConfigureAwait(false);
        var stream = new List<int>();
        await foreach (var item in mediator.StreamAsync(new TestStreamQuery(), cancellation.Token).ConfigureAwait(false))
        {
            stream.Add(item);
        }

        await foreach (var item in mediator.StreamAsync(new TestStreamQuery(), "stream-tag", cancellation.Token).ConfigureAwait(false))
        {
            stream.Add(item);
        }

        result.Should().Be(42);
        taggedResult.Should().Be(42);
        stream.Should().Equal(42, 42);
        mediator.Settings.Should().HaveCount(4);
        mediator.Settings[0].Should().BeNull();
        mediator.Settings[1]!.Routing.Tags.Should().Equal("query-tag");
        mediator.Settings[2].Should().BeNull();
        mediator.Settings[3]!.Routing.Tags.Should().Equal("stream-tag");
        mediator.Tokens.Should().OnlyContain(token => token == cancellation.Token);
    }

    /// <summary>
    ///     Verifies event extensions forward default, untyped tagged, and typed tagged settings.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task EventExtensions_ShouldForwardDefaultAndTaggedSettings()
    {
        var mediator = new RecordingEventMediator();
        using var cancellation = new CancellationTokenSource();
        var @event = new TestEvent();

        await mediator.PublishAsync((IEvent)@event, cancellation.Token).ConfigureAwait(false);
        await mediator.PublishAsync((IEvent)@event, "event-tag", cancellation.Token).ConfigureAwait(false);
        await mediator.PublishAsync(@event, "typed-tag", cancellation.Token).ConfigureAwait(false);

        mediator.Settings.Should().HaveCount(3);
        mediator.Settings[0].Should().BeNull();
        mediator.Settings[1]!.Routing.Tags.Should().Equal("event-tag");
        mediator.Settings[2]!.Routing.Tags.Should().Equal("typed-tag");
        mediator.Tokens.Should().OnlyContain(token => token == cancellation.Token);
    }

    private sealed record TestCommand : ICommand;

    private sealed record TestResultCommand : ICommand<int>;

    private sealed record TestQuery : IQuery<int>;

    private sealed record TestStreamQuery : IStreamQuery<int>;

    private sealed record TestEvent : IEvent;

    private sealed class RecordingCommandMediator : ICommandMediator
    {
        public List<CommandMediationSettings?> Settings { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task SendAsync(
            ICommand command,
            CommandMediationSettings? commandMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            Settings.Add(commandMediationSettings);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task<TCommandResult> SendAsync<TCommandResult>(
            ICommand<TCommandResult> command,
            CommandMediationSettings? commandMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            Settings.Add(commandMediationSettings);
            Tokens.Add(cancellationToken);
            return Task.FromResult((TCommandResult)(object)42);
        }
    
        /// <inheritdoc />
        public Task<MediationResult> TrySendAsync(
            ICommand command,
            CommandMediationSettings? commandMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationResult.Succeeded());
        }

        /// <inheritdoc />
        public Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(
            ICommand<TCommandResult> command,
            CommandMediationSettings? commandMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationResult<TCommandResult>.Succeeded(default!));
        }

        /// <inheritdoc />
        public Task<MediationDecision> EvaluateAsync(
            ICommand command,
            CommandMediationSettings? commandMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationDecision.Allowed);
        }
    }

    private sealed class RecordingQueryMediator : IQueryMediator
    {
        public List<QueryMediationSettings?> Settings { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task<TQueryResult> QueryAsync<TQueryResult>(
            IQuery<TQueryResult> query,
            QueryMediationSettings? queryMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            Settings.Add(queryMediationSettings);
            Tokens.Add(cancellationToken);
            return Task.FromResult((TQueryResult)(object)42);
        }

        public IAsyncEnumerable<TQueryResult> StreamAsync<TQueryResult>(
            IStreamQuery<TQueryResult> query,
            QueryMediationSettings? queryMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            Settings.Add(queryMediationSettings);
            Tokens.Add(cancellationToken);
            return YieldAsync((TQueryResult)(object)42, cancellationToken);
        }

        private static async IAsyncEnumerable<T> YieldAsync<T>(
            T item,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    
        /// <inheritdoc />
        public Task<MediationResult<TQueryResult>> TryQueryAsync<TQueryResult>(
            IQuery<TQueryResult> query,
            QueryMediationSettings? queryMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationResult<TQueryResult>.Succeeded(default!));
        }

        /// <inheritdoc />
        public Task<MediationDecision> EvaluateAsync(
            IQuery query,
            QueryMediationSettings? queryMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MediationDecision.Allowed);
        }
    }

    private sealed class RecordingEventMediator : IEventMediator
    {
        public List<EventMediationSettings?> Settings { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task PublishAsync(
            IEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
        {
            Settings.Add(eventMediationSettings);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task PublishAsync<TEvent>(
            TEvent @event,
            EventMediationSettings? eventMediationSettings = null,
            CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            Settings.Add(eventMediationSettings);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
