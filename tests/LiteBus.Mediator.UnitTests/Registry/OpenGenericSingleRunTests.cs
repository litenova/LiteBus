using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Registry;

/// <summary>
///     Verifies that an open generic guard constrained to <see cref="ICommand" /> runs once per command even when a
///     handler closed over <see cref="ICommand" /> makes the base interface a registered message too.
/// </summary>
/// <remarks>
///     The guard closes over both the base interface and the concrete command, which looks like a double run waiting
///     to happen. The close over the interface links to the interface descriptor, while the close over the command is
///     the one linked to the command, so one mediation runs the guard once. These tests pin that single run and the
///     single plan entry from both registration orders and from assembly scanning.
/// </remarks>
[Collection("Sequential")]
public sealed class OpenGenericSingleRunTests : LiteBusTestBase
{
    [Fact]
    public async Task An_open_generic_guard_before_the_base_interface_handler_runs_once()
    {
        var recorder = new SingleRunRecorder();
        var provider = BuildProvider(
            recorder,
            typeof(SingleRunCommand),
            typeof(SingleRunCommandHandler),
            typeof(SingleRunGuard<>),
            typeof(SingleRunPreHandler));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SingleRunCommand()).ConfigureAwait(false);

        AssertGuardRanOnce(provider, recorder);
    }

    [Fact]
    public async Task An_open_generic_guard_after_the_base_interface_handler_runs_once()
    {
        var recorder = new SingleRunRecorder();
        var provider = BuildProvider(
            recorder,
            typeof(SingleRunCommand),
            typeof(SingleRunCommandHandler),
            typeof(SingleRunPreHandler),
            typeof(SingleRunGuard<>));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SingleRunCommand()).ConfigureAwait(false);

        AssertGuardRanOnce(provider, recorder);
    }

    [Fact]
    public async Task An_open_generic_guard_before_a_closed_guard_runs_once()
    {
        var recorder = new SingleRunRecorder();
        var provider = BuildProvider(
            recorder,
            typeof(SingleRunCommand),
            typeof(SingleRunCommandHandler),
            typeof(SingleRunGuard<>),
            typeof(SingleRunClosedGuard));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SingleRunCommand()).ConfigureAwait(false);

        AssertGuardRanOnce(provider, recorder);
    }

    [Fact]
    public async Task An_open_generic_guard_after_a_closed_guard_runs_once()
    {
        var recorder = new SingleRunRecorder();
        var provider = BuildProvider(
            recorder,
            typeof(SingleRunCommand),
            typeof(SingleRunCommandHandler),
            typeof(SingleRunClosedGuard),
            typeof(SingleRunGuard<>));

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SingleRunCommand()).ConfigureAwait(false);

        AssertGuardRanOnce(provider, recorder);
    }

    [Fact]
    public async Task A_scan_that_discovers_both_runs_the_guard_once()
    {
        var recorder = new SingleRunRecorder();
        var provider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(RegisterScanOrder);
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SingleRunCommand()).ConfigureAwait(false);

        AssertGuardRanOnce(provider, recorder);
    }

    [Fact]
    public void Registering_in_scan_order_through_the_scan_entry_point_lists_the_guard_once()
    {
        IMessageWriter writer = new MessageRegistry();

        foreach (var type in ScanOrderTypes())
        {
            writer.RegisterFromScan(type);
        }

        // RegisterFromScan is the exact primitive RegisterFromAssembly calls per type; it differs from Register only
        // in the origin flags strict declaration mode reads, so this is the scan path without the rest of the
        // assembly.
        AssertGuardExplainedOnce((IMessageReader) writer);
    }

    /// <summary>
    ///     Registers this test's fixtures in assembly scan order, replaying what <c>RegisterFromAssembly</c> reaches.
    /// </summary>
    /// <param name="builder">The command module builder.</param>
    /// <remarks>
    ///     A full scan of this assembly is not usable here: it would also sweep every other test's fixtures, including
    ///     shortcuts that answer and open generics that need foreign recorders. The replay enumerates the same
    ///     <see cref="AssemblyTypeScanner.GetLoadableTypes" /> order filtered to these fixtures, so the relative order
    ///     of the command, the guard, and the base-interface handlers is the order a scan produces.
    /// </remarks>
    internal static void RegisterScanOrder(CommandModuleBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var type in ScanOrderTypes())
        {
            builder.Register(type);
        }
    }

    /// <summary>
    ///     Lists this test's fixtures in the order an assembly scan reaches them.
    /// </summary>
    /// <returns>The registrable fixture types, in scan order.</returns>
    internal static IReadOnlyList<Type> ScanOrderTypes()
    {
        return AssemblyTypeScanner.GetLoadableTypes(typeof(OpenGenericSingleRunTests).Assembly)
            .Where(static type => type is { IsClass: true, IsAbstract: false }
                                  && type.Name.StartsWith("SingleRun", StringComparison.Ordinal)
                                  && CommandModuleBuilder.IsCommandConstruct(type))
            .ToList();
    }

    /// <summary>
    ///     Builds a provider registering the given types in the given order.
    /// </summary>
    /// <param name="recorder">The recorder shared with the fixtures.</param>
    /// <param name="registrations">The types to register, in order.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(SingleRunRecorder recorder, params Type[] registrations)
    {
        return new ServiceCollection()
            .AddSingleton(recorder)
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
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Asserts the open generic guard ran once, as the close over the command, and is explained once.
    /// </summary>
    /// <param name="provider">The provider that mediated the command.</param>
    /// <param name="recorder">The recorder shared with the fixtures.</param>
    private static void AssertGuardRanOnce(ServiceProvider provider, SingleRunRecorder recorder)
    {
        recorder.Ran.Where(static entry => entry.StartsWith("guard:", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Be("guard:SingleRunCommand");

        AssertGuardExplainedOnce(provider.GetRequiredService<IMessageReader>());
    }

    /// <summary>
    ///     Asserts the registry explains the guard once, as a direct close over the command.
    /// </summary>
    /// <param name="reader">The registry to explain.</param>
    private static void AssertGuardExplainedOnce(IMessageReader reader)
    {
        var plan = reader.Explain(typeof(SingleRunCommand));

        var guardStep = plan.Steps.Should()
            .ContainSingle(static step => step.HandlerType.IsGenericType
                                          && step.HandlerType.GetGenericTypeDefinition() == typeof(SingleRunGuard<>))
            .Subject;

        guardStep.IsClosedOpenGeneric.Should().BeTrue();
        guardStep.IsIndirect.Should().BeFalse();

        // The guard closes over the base interface too; that close belongs to the interface descriptor, which is why
        // closing twice still runs once.
        var closures = reader.OpenGenericClosures[typeof(SingleRunGuard<>)];
        closures.Should().HaveCount(2);
        closures.Should().Contain(typeof(SingleRunCommand));
        closures.Should().Contain(typeof(ICommand));
    }
}

/// <summary>
///     Counts how often each single-run fixture executed, so a double run shows up as a count of two.
/// </summary>
public sealed class SingleRunRecorder
{
    /// <summary>
    ///     Gets the fixture entries that ran, in order.
    /// </summary>
    public List<string> Ran { get; } = [];
}

/// <summary>
///     A command guarded by one open generic guard while a handler closed over <see cref="ICommand" /> also applies.
/// </summary>
internal sealed class SingleRunCommand : ICommand;

/// <summary>
///     Handles <see cref="SingleRunCommand" /> and records that it ran.
/// </summary>
internal sealed class SingleRunCommandHandler : ICommandHandler<SingleRunCommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SingleRunRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleRunCommandHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SingleRunCommandHandler(SingleRunRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task HandleAsync(SingleRunCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        _recorder.Ran.Add("main");
        return Task.CompletedTask;
    }
}

/// <summary>
///     An open generic guard constrained to commands, standing in for the integrator's cross-cutting guard.
/// </summary>
/// <typeparam name="TMessage">The command type, bound at registration.</typeparam>
internal sealed class SingleRunGuard<TMessage> : IMessageGuard<TMessage>
    where TMessage : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SingleRunRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleRunGuard{TMessage}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SingleRunGuard(SingleRunRecorder recorder)
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
///     A pre-handler closed over <see cref="ICommand" />, which makes the base interface a registered message.
/// </summary>
internal sealed class SingleRunPreHandler : ICommandPreHandler<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SingleRunRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleRunPreHandler" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SingleRunPreHandler(SingleRunRecorder recorder)
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
///     A guard closed over <see cref="ICommand" />, the second base-interface shape beside the pre-handler.
/// </summary>
internal sealed class SingleRunClosedGuard : IMessageGuard<ICommand>
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly SingleRunRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleRunClosedGuard" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public SingleRunClosedGuard(SingleRunRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Verdict> DecideAsync(ICommand message, CancellationToken cancellationToken = default)
    {
        _recorder.Ran.Add("closed-guard");
        return Task.FromResult(Verdict.Allow);
    }
}
