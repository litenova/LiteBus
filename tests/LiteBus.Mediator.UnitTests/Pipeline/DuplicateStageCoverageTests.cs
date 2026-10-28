using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Registry;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that the composition summary reports a handler type covering one message's stage twice.
/// </summary>
/// <remarks>
///     The pipeline runs the indirect and direct sets together, so one handler type present in both runs twice. One
///     open generic registration never does: when it closes over both a base interface and the concrete message, the
///     direct closure replaces the indirect one whatever the registration order. A handler class that implements both
///     the base and the concrete contract itself still runs twice, and the summary names that at startup.
/// </remarks>
[Collection("Sequential")]
public sealed class DuplicateStageCoverageTests : LiteBusTestBase
{
    [Fact]
    public async Task An_open_generic_guard_closed_over_the_base_interface_before_the_command_runs_once()
    {
        var recorder = new DoubleCoverRecorder();
        var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    // The command arrives last, after the base interface became a message and the guard closed over
                    // it. The closure over the interface would link to the command indirectly; the direct closure
                    // replaces it.
                    builder.Register<DoubleCoverPreHandler>();
                    builder.Register(typeof(DoubleCoverGuard<>));
                    builder.Register<DoubleCoverCommand>();
                    builder.Register<DoubleCoverCommandHandler>();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusCompositionSummary>().DuplicateStageCoverages.Should().BeEmpty();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DoubleCoverCommand()).ConfigureAwait(false);

        recorder.Ran.Where(static entry => entry.StartsWith("guard:", StringComparison.Ordinal))
            .Should().Equal("guard:DoubleCoverCommand");

        provider.GetRequiredService<IMessageReader>().Explain(typeof(DoubleCoverCommand)).Steps
            .Where(static step => step.HandlerType.IsGenericType
                                  && step.HandlerType.GetGenericTypeDefinition() == typeof(DoubleCoverGuard<>))
            .Select(static step => step.IsIndirect)
            .Should().Equal(false);
    }

    [Fact]
    public async Task A_class_implementing_the_base_and_the_concrete_guard_is_reported_and_runs_twice()
    {
        var recorder = new DoubleCoverRecorder();
        var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<DoubleCoverCommand>();
                    builder.Register<DoubleCoverCommandHandler>();
                    builder.Register<DoubleCoverDualGuard>();
                });
            })
            .BuildServiceProvider();

        var summary = provider.GetRequiredService<LiteBusCompositionSummary>();

        summary.DuplicateStageCoverages.Should().ContainSingle()
            .Which.Should().Be(new DuplicateStageCoverage(
                typeof(DoubleCoverDualGuard),
                typeof(DoubleCoverCommand),
                typeof(IMessageGuard<DoubleCoverCommand>),
                typeof(IMessageGuard<ICommand>)));

        summary.ToString().Should().Contain("duplicate stage coverage");
        summary.ToString().Should().Contain("DoubleCoverDualGuard");

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DoubleCoverCommand()).ConfigureAwait(false);

        // The report names a real double run: the base contract runs first, then the concrete one.
        recorder.Ran.Where(static entry => entry.StartsWith("dual-guard:", StringComparison.Ordinal))
            .Should().Equal("dual-guard:ICommand", "dual-guard:DoubleCoverCommand");
    }

    [Fact]
    public async Task Open_generic_post_error_and_completion_handlers_run_once_beside_closed_base_handlers()
    {
        var recorder = new DoubleCoverRecorder();
        var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    // The command arrives last here as well, so each open generic has already closed over ICommand.
                    builder.Register<DoubleCoverClosedPostHandler>();
                    builder.Register<DoubleCoverClosedErrorHandler>();
                    builder.Register<DoubleCoverClosedCompletionHandler>();
                    builder.Register(typeof(DoubleCoverPostHandler<>));
                    builder.Register(typeof(DoubleCoverErrorHandler<>));
                    builder.Register(typeof(DoubleCoverCompletionHandler<>));
                    builder.Register<DoubleCoverCommand>();
                    builder.Register<DoubleCoverCommandHandler>();
                });
            })
            .BuildServiceProvider();

        provider.GetRequiredService<LiteBusCompositionSummary>().DuplicateStageCoverages.Should().BeEmpty();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DoubleCoverCommand()).ConfigureAwait(false);

        // Each open generic runs once, directly; the separately registered closed base handlers still run.
        recorder.Ran.Where(static entry => entry.StartsWith("post:", StringComparison.Ordinal))
            .Should().Equal("post:DoubleCoverCommand");
        recorder.Ran.Where(static entry => entry.StartsWith("completion:", StringComparison.Ordinal))
            .Should().Equal("completion:DoubleCoverCommand");
        recorder.Ran.Should().Contain("closed-post").And.Contain("closed-completion");

        var failing = () => provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DoubleCoverCommand { ShouldThrow = true });

        await failing.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        recorder.Ran.Where(static entry => entry.StartsWith("error:", StringComparison.Ordinal))
            .Should().Equal("error:DoubleCoverCommand");
        recorder.Ran.Should().Contain("closed-error");
    }

    [Fact]
    public void Single_run_shapes_are_not_reported()
    {
        // Both explicit orders with the command first: the close over the base interface belongs to the interface
        // descriptor, so the command carries one entry per handler.
        BuildDoubleCoverSummary(
                typeof(DoubleCoverCommand),
                typeof(DoubleCoverCommandHandler),
                typeof(DoubleCoverGuard<>),
                typeof(DoubleCoverPreHandler))
            .DuplicateStageCoverages.Should().BeEmpty();

        BuildDoubleCoverSummary(
                typeof(DoubleCoverCommand),
                typeof(DoubleCoverCommandHandler),
                typeof(DoubleCoverPreHandler),
                typeof(DoubleCoverGuard<>))
            .DuplicateStageCoverages.Should().BeEmpty();

        // The step one shapes, explicit and scan-replayed: closing over the base interface must not report.
        BuildSingleRunSummary(builder =>
            {
                builder.Register<SingleRunCommand>();
                builder.Register<SingleRunCommandHandler>();
                builder.Register(typeof(SingleRunGuard<>));
                builder.Register<SingleRunPreHandler>();
                builder.Register<SingleRunClosedGuard>();
            })
            .DuplicateStageCoverages.Should().BeEmpty();

        BuildSingleRunSummary(OpenGenericSingleRunTests.RegisterScanOrder)
            .DuplicateStageCoverages.Should().BeEmpty();
    }

    /// <summary>
    ///     Builds the composition summary for the double-cover fixtures registered in the given order.
    /// </summary>
    /// <param name="registrations">The types to register, in order.</param>
    /// <returns>The populated composition summary.</returns>
    private static LiteBusCompositionSummary BuildDoubleCoverSummary(params Type[] registrations)
    {
        return new ServiceCollection()
            .AddSingleton(new DoubleCoverRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    foreach (var type in registrations)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider()
            .GetRequiredService<LiteBusCompositionSummary>();
    }

    /// <summary>
    ///     Builds the composition summary for the single-run fixtures registered by the given configuration.
    /// </summary>
    /// <param name="configure">The command module configuration.</param>
    /// <returns>The populated composition summary.</returns>
    private static LiteBusCompositionSummary BuildSingleRunSummary(Action<CommandModuleBuilder> configure)
    {
        return new ServiceCollection()
            .AddSingleton(new SingleRunRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(configure);
            })
            .BuildServiceProvider()
            .GetRequiredService<LiteBusCompositionSummary>();
    }
}

/// <summary>
///     Counts how often each double-cover fixture executed, so a double run shows up as a count of two.
/// </summary>
public sealed class DoubleCoverRecorder
{
    /// <summary>
    ///     Gets the fixture entries that ran, in order.
    /// </summary>
    public List<string> Ran { get; } = [];
}

/// <summary>
///     A command whose registration order decides whether its handlers link once or twice.
/// </summary>
internal sealed class DoubleCoverCommand : ICommand
{
    /// <summary>
    ///     Gets or sets a value indicating whether the main handler throws, so the error path can be exercised.
    /// </summary>
    public bool ShouldThrow { get; set; }
}

/// <summary>
///     Handles <see cref="DoubleCoverCommand" />, throwing when the command asks for the error path.
/// </summary>
internal sealed class DoubleCoverCommandHandler : ICommandHandler<DoubleCoverCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverCommandHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(DoubleCoverCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ShouldThrow)
        {
            throw new InvalidOperationException("handler failed");
        }

        _recorder.Ran.Add("main");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An open generic guard that covers the command twice when the command is registered last.
/// </summary>
/// <typeparam name="TMessage">The command type, bound at registration.</typeparam>
internal sealed class DoubleCoverGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverGuard(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(TMessage message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add($"guard:{typeof(TMessage).Name}");
        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     A guard class that implements both the base and the concrete guard contract, so it covers the command twice.
/// </summary>
internal sealed class DoubleCoverDualGuard : IMessageGuard<ICommand>, IMessageGuard<DoubleCoverCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverDualGuard" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverDualGuard(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("dual-guard:ICommand");
        return Task.FromResult(Verdict.Allow);
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(DoubleCoverCommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("dual-guard:DoubleCoverCommand");
        return Task.FromResult(Verdict.Allow);
    }
}

/// <summary>
///     A pre-handler closed over <see cref="ICommand" />, which makes the base interface a registered message.
/// </summary>
internal sealed class DoubleCoverPreHandler : ICommandPreHandler<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverPreHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverPreHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PreHandleAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("prehandler");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An open generic post-handler that covers the command twice when the command is registered last.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
internal sealed class DoubleCoverPostHandler<TCommand> : ICommandPostHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverPostHandler{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverPostHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(TCommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add($"post:{typeof(TCommand).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A post-handler closed over <see cref="ICommand" />, the indirect side of the post-handler double.
/// </summary>
internal sealed class DoubleCoverClosedPostHandler : ICommandPostHandler<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverClosedPostHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverClosedPostHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task PostHandleAsync(ICommand message, object? messageResult, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("closed-post");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An open generic error handler that covers the command twice when the command is registered last.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
internal sealed class DoubleCoverErrorHandler<TCommand> : ICommandErrorHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverErrorHandler{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverErrorHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleErrorAsync(MessageErrorContext<TCommand, object> context, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add($"error:{typeof(TCommand).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An error handler closed over <see cref="ICommand" />, the indirect side of the error-handler double.
/// </summary>
internal sealed class DoubleCoverClosedErrorHandler : ICommandErrorHandler<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverClosedErrorHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverClosedErrorHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleErrorAsync(MessageErrorContext<ICommand, object> context, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("closed-error");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An open generic completion handler that covers the command twice when the command is registered last.
/// </summary>
/// <typeparam name="TCommand">The command type, bound at registration.</typeparam>
internal sealed class DoubleCoverCompletionHandler<TCommand> : ICommandCompletionHandler<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverCompletionHandler{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverCompletionHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<TCommand> context, CancellationToken cancellationToken)
    {
        _recorder.Ran.Add($"completion:{typeof(TCommand).Name}");
        return Task.CompletedTask;
    }
}

/// <summary>
///     A completion handler closed over <see cref="ICommand" />, the indirect side of the completion double.
/// </summary>
internal sealed class DoubleCoverClosedCompletionHandler : ICommandCompletionHandler<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly DoubleCoverRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DoubleCoverClosedCompletionHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public DoubleCoverClosedCompletionHandler(DoubleCoverRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleCompletionAsync(MessageCompletionContext<ICommand> context, CancellationToken cancellationToken)
    {
        _recorder.Ran.Add("closed-completion");
        return Task.CompletedTask;
    }
}
