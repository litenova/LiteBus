using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Batching;

/// <summary>
///     Verifies argument validation, empty batch handling, and nested batch refusal for <see cref="ICommandBatchMediator" />.
/// </summary>
public sealed class CommandBatchValidationTests
{
    /// <summary>
    ///     Verifies that sending a null command list throws.
    /// </summary>
    [Fact]
    public async Task SendManyAsync_NullCommands_ShouldThrowArgumentNullException()
    {
        var mediator = BuildProvider().GetRequiredService<ICommandBatchMediator>();

        var act = () => mediator.SendManyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*commands*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that attempting a null command list throws.
    /// </summary>
    [Fact]
    public async Task TrySendManyAsync_NullCommands_ShouldThrowArgumentNullException()
    {
        var mediator = BuildProvider().GetRequiredService<ICommandBatchMediator>();

        var act = () => mediator.TrySendManyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithMessage("*commands*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that a null element throws an exception naming the index.
    /// </summary>
    [Fact]
    public async Task SendManyAsync_NullElement_ShouldThrowArgumentExceptionNamingIndex()
    {
        var mediator = BuildProvider().GetRequiredService<ICommandBatchMediator>();
        IReadOnlyList<ICommand> commands = [new BatchProbeCommand(), null!, new BatchProbeCommand()];

        var act = () => mediator.SendManyAsync(commands);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*index 1*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that attempting a batch with a null element throws an exception naming the index.
    /// </summary>
    [Fact]
    public async Task TrySendManyAsync_NullElement_ShouldThrowArgumentExceptionNamingIndex()
    {
        var mediator = BuildProvider().GetRequiredService<ICommandBatchMediator>();
        IReadOnlyList<ICommand> commands = [new BatchProbeCommand(), null!, new BatchProbeCommand()];

        var act = () => mediator.TrySendManyAsync(commands);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*index 1*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies that an empty batch commits with no entries and calls no unit of work method.
    /// </summary>
    [Fact]
    public async Task TrySendManyAsync_EmptyList_ShouldReturnCommittedWithoutEntries()
    {
        var recorder = new BatchUnitOfWorkRecorder();
        var mediator = BuildProvider(recorder).GetRequiredService<ICommandBatchMediator>();

        var result = await mediator.TrySendManyAsync([]).ConfigureAwait(false);

        result.Outcome.Should().Be(CommandBatchOutcome.Committed);
        result.Entries.Should().BeEmpty();
        result.FailedIndex.Should().BeNull();
        recorder.Begins.Should().Be(0);
        recorder.Commits.Should().Be(0);
        recorder.Rollbacks.Should().Be(0);
    }

    /// <summary>
    ///     Verifies that sending an empty batch completes without calling any unit of work method.
    /// </summary>
    [Fact]
    public async Task SendManyAsync_EmptyList_ShouldCompleteWithoutUnitOfWorkCalls()
    {
        var recorder = new BatchUnitOfWorkRecorder();
        var mediator = BuildProvider(recorder).GetRequiredService<ICommandBatchMediator>();

        await mediator.SendManyAsync([]).ConfigureAwait(false);

        recorder.Begins.Should().Be(0);
        recorder.Commits.Should().Be(0);
        recorder.Rollbacks.Should().Be(0);
    }

    /// <summary>
    ///     Verifies that a batch started from inside another batch is refused.
    /// </summary>
    [Fact]
    public async Task SendManyAsync_NestedBatch_ShouldThrowInvalidOperationException()
    {
        var mediator = BuildProvider().GetRequiredService<ICommandBatchMediator>();

        var act = () => mediator.SendManyAsync([new OuterBatchCommand()]);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inside another command batch*").ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds a provider registering the probe commands, with an optional recording unit of work.
    /// </summary>
    /// <param name="recorder">The recorder shared with the unit of work, or <see langword="null" /> for no unit of work.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(BatchUnitOfWorkRecorder? recorder = null)
    {
        var services = new ServiceCollection();

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<BatchProbeCommand>();
                    builder.Register<BatchProbeHandler>();
                    builder.Register<OuterBatchCommand>();
                    builder.Register<NestedBatchHandler>();

                    if (recorder is not null)
                    {
                        builder.UseUnitOfWork<RecordingBoundaryUnitOfWork>();
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     A command that fills batch positions without side effects.
    /// </summary>
    private sealed class BatchProbeCommand : ICommand
    {
    }

    /// <summary>
    ///     Handles the probe command without side effects.
    /// </summary>
    private sealed class BatchProbeHandler : ICommandHandler<BatchProbeCommand>
    {
        /// <inheritdoc />
        public Task HandleAsync(BatchProbeCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A command whose handler starts another batch while the outer batch runs.
    /// </summary>
    private sealed class OuterBatchCommand : ICommand
    {
    }

    /// <summary>
    ///     Starts a nested batch, which the batch mediator refuses.
    /// </summary>
    private sealed class NestedBatchHandler : ICommandHandler<OuterBatchCommand>
    {
        /// <summary>
        ///     The batch mediator the handler calls into the running batch.
        /// </summary>
        private readonly ICommandBatchMediator _batchMediator;

        /// <summary>
        ///     Initializes a new instance of the <see cref="NestedBatchHandler" /> class.
        /// </summary>
        /// <param name="batchMediator">The batch mediator the handler calls into the running batch.</param>
        public NestedBatchHandler(ICommandBatchMediator batchMediator)
        {
            ArgumentNullException.ThrowIfNull(batchMediator);

            _batchMediator = batchMediator;
        }

        /// <inheritdoc />
        public async Task HandleAsync(OuterBatchCommand message, CancellationToken cancellationToken = default)
        {
            await _batchMediator.SendManyAsync([], null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Counts unit of work calls during one batch validation test run.
    /// </summary>
    private sealed class BatchUnitOfWorkRecorder
    {
        /// <summary>
        ///     Gets or sets the number of observed transaction opens.
        /// </summary>
        public int Begins { get; set; }

        /// <summary>
        ///     Gets or sets the number of observed commits.
        /// </summary>
        public int Commits { get; set; }

        /// <summary>
        ///     Gets or sets the number of observed rollbacks.
        /// </summary>
        public int Rollbacks { get; set; }
    }

    /// <summary>
    ///     A unit of work that records boundary calls instead of touching storage.
    /// </summary>
    private sealed class RecordingBoundaryUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
    {
        /// <summary>
        ///     The recorder shared with the test.
        /// </summary>
        private readonly BatchUnitOfWorkRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RecordingBoundaryUnitOfWork" /> class.
        /// </summary>
        /// <param name="recorder">The recorder shared with the test.</param>
        public RecordingBoundaryUnitOfWork(BatchUnitOfWorkRecorder recorder)
        {
            ArgumentNullException.ThrowIfNull(recorder);

            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task BeginAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Begins++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Commits++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Rollbacks++;
            return Task.CompletedTask;
        }
    }
}
