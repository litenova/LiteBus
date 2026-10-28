using System.Collections.Concurrent;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies that a stream supplied by a refusal mapper is enumerated through the same runner as the handler's
///     stream, so a fault in it reaches the error stage instead of escaping raw.
/// </summary>
[Collection("Sequential")]
public sealed class RefusalMappedStreamFaultTests : LiteBusTestBase
{
    [Fact]
    public async Task Fault_in_a_mapped_refusal_stream_reaches_error_handlers()
    {
        var completions = new CompletionRecorder();
        var faults = new StreamFaultRecorder();
        var mediator = BuildProvider(completions, faults).GetRequiredService<IQueryMediator>();
        var items = new List<string>();

        await foreach (var item in mediator.StreamAsync(new GuardedStreamQuery { ShouldDeny = true }).ConfigureAwait(false))
        {
            items.Add(item);
        }

        // Items produced before the fault still reach the caller; the fault ends enumeration rather than replaying.
        items.Should().Equal("first");
        faults.Observed.Should().ContainSingle().Which.Should().Be("the mapped stream faulted");

        var observed = completions.Observed.Should().ContainSingle().Subject.Context;
        observed.Outcome.Should().Be(MediationOutcome.Failed);
        observed.Exception.Should().BeNull();
    }

    /// <summary>
    ///     Builds a provider whose guard denies the stream query and whose mapper answers with a faulting stream.
    /// </summary>
    /// <param name="completions">The recorder shared with the completion handler.</param>
    /// <param name="faults">The recorder shared with the error handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CompletionRecorder completions, StreamFaultRecorder faults)
    {
        var services = new ServiceCollection();
        services.AddSingleton(completions);
        services.AddSingleton(faults);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddQueries(builder =>
                {
                    builder.Register<GuardedStreamQuery>();
                    builder.Register<GuardedStreamGuard>();
                    builder.Register<GuardedStreamMapper>();
                    builder.Register<GuardedStreamHandler>();
                    builder.Register<GuardedStreamErrorHandler>();
                    builder.Register<GuardedStreamCompletionHandler>();
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Collects the fault messages the error handler observed.
/// </summary>
internal sealed class StreamFaultRecorder
{
    /// <summary>
    ///     Gets the observed fault messages, in the order the error handler ran.
    /// </summary>
    public ConcurrentQueue<string> Observed { get; } = new();
}

/// <summary>
///     A stream query the test denies through its guard.
/// </summary>
internal sealed class GuardedStreamQuery : IStreamQuery<string>
{
    /// <summary>
    ///     Gets or sets a value indicating whether the guard refuses the query.
    /// </summary>
    public bool ShouldDeny { get; set; }
}

/// <summary>
///     Refuses the query when the query asks for it.
/// </summary>
internal sealed class GuardedStreamGuard : IQueryGuard<GuardedStreamQuery>
{
    /// <inheritdoc />
    public Task<Verdict> DecideAsync(GuardedStreamQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        return Task.FromResult(message.ShouldDeny
            ? Verdict.Deny("not permitted")
            : Verdict.Allow);
    }
}

/// <summary>
///     Answers a refused query with a stream that faults after its first item.
/// </summary>
internal sealed class GuardedStreamMapper : IStreamQueryRefusalMapper<GuardedStreamQuery, string>
{
    /// <inheritdoc />
    public IAsyncEnumerable<string> Map(GuardedStreamQuery message, Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(message);
        return FailingStreamAsync();
    }

    /// <summary>
    ///     Yields one item and then faults, the way a failing projection would.
    /// </summary>
    /// <returns>A stream that faults after its first item.</returns>
    private static async IAsyncEnumerable<string> FailingStreamAsync()
    {
        yield return "first";
        await Task.CompletedTask.ConfigureAwait(false);
        throw new InvalidOperationException("the mapped stream faulted");
    }
}

/// <summary>
///     Streams nothing. The guard denies before the main handler resolves, so this never runs.
/// </summary>
internal sealed class GuardedStreamHandler : IStreamQueryHandler<GuardedStreamQuery, string>
{
    /// <inheritdoc />
    public IAsyncEnumerable<string> StreamAsync(GuardedStreamQuery message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return NeverEnumeratedAsync();
    }

    /// <summary>
    ///     Produces the empty stream the denied mediation never enumerates.
    /// </summary>
    /// <returns>An empty asynchronous sequence.</returns>
    private static async IAsyncEnumerable<string> NeverEnumeratedAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}

/// <summary>
///     Records the fault and recovers, so enumeration ends without rethrowing.
/// </summary>
internal sealed class GuardedStreamErrorHandler : IQueryErrorHandler<GuardedStreamQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly StreamFaultRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GuardedStreamErrorHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public GuardedStreamErrorHandler(StreamFaultRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleErrorAsync(
        MessageErrorContext<GuardedStreamQuery, object> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(context.Exception.Message);
        context.Outcome = MessageErrorOutcome.Handled;
        return Task.CompletedTask;
    }
}

/// <summary>
///     Records how the stream mediation ended.
/// </summary>
internal sealed class GuardedStreamCompletionHandler : IQueryCompletionHandler<GuardedStreamQuery>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly CompletionRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GuardedStreamCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public GuardedStreamCompletionHandler(CompletionRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(
        MessageCompletionContext<GuardedStreamQuery> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        _recorder.Observed.Enqueue(("stream", context.AsUntyped()));
        return Task.CompletedTask;
    }
}
