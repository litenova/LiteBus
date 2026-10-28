using System.Collections.Concurrent;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies that an open generic closure over a base type reaches an already-committed derived message, so
///     indirect coverage does not depend on registration order.
/// </summary>
[Collection("Sequential")]
public sealed class OpenGenericOrderIndependenceTests : LiteBusTestBase
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Open_generic_shortcut_covers_a_result_derived_message_whichever_registered_first(int order)
    {
        var recorder = new ShortcutCallRecorder();
        var provider = BuildProvider(recorder, order);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new OrderDerivedCommand()).ConfigureAwait(false);

        result.Should().Be(7);

        // The untyped shortcut cannot close over the result-bearing derived message directly, so the closure over
        // the void base is the only one that can run for it, indirectly. Before the fix the closure linked only to
        // the base when the derived message had already committed, and the shortcut silently skipped it.
        recorder.Calls.Should().ContainSingle().Which.Should().Be<OrderBaseCommand>();
    }

    /// <summary>
    ///     Builds a provider registering the base command, the derived command, and the open generic shortcut in one
    ///     of two orders.
    /// </summary>
    /// <param name="recorder">The recorder shared with the shortcut.</param>
    /// <param name="order">Zero for base, derived, shortcut; one for base, shortcut, derived.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(ShortcutCallRecorder recorder, int order)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<OrderBaseCommand>();

                    if (order == 0)
                    {
                        builder.Register<OrderDerivedCommand>();
                        builder.Register(typeof(RecordingOpenGenericShortcut<>));
                    }
                    else
                    {
                        builder.Register(typeof(RecordingOpenGenericShortcut<>));
                        builder.Register<OrderDerivedCommand>();
                    }

                    builder.Register<OrderDerivedCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}

/// <summary>
///     Collects the message types an open generic shortcut closed over and ran for.
/// </summary>
internal sealed class ShortcutCallRecorder
{
    /// <summary>
    ///     Gets the closed message types the shortcut ran for, in run order.
    /// </summary>
    public ConcurrentQueue<Type> Calls { get; } = new();
}

/// <summary>
///     A void base command the open generic shortcut closes over directly.
/// </summary>
internal class OrderBaseCommand : ICommand;

/// <summary>
///     A result-bearing derived command the untyped shortcut cannot close over directly.
/// </summary>
internal sealed class OrderDerivedCommand : OrderBaseCommand, ICommand<int>;

/// <summary>
///     Answers the derived command with a fixed value.
/// </summary>
internal sealed class OrderDerivedCommandHandler : ICommandHandler<OrderDerivedCommand, int>
{
    /// <inheritdoc />
    public Task<int> HandleAsync(OrderDerivedCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Task.FromResult(7);
    }
}

/// <summary>
///     Records the closed message type and lets the mediation proceed.
/// </summary>
/// <typeparam name="TCommand">The message type the registry closed this shortcut over.</typeparam>
internal sealed class RecordingOpenGenericShortcut<TCommand> : ICommandShortcut<TCommand>
    where TCommand : ICommand
{
    /// <summary>
    ///     The recorder shared with the test.
    /// </summary>
    private readonly ShortcutCallRecorder _recorder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RecordingOpenGenericShortcut{TCommand}" /> class.
    /// </summary>
    /// <param name="recorder">The recorder shared with the test.</param>
    public RecordingOpenGenericShortcut(ShortcutCallRecorder recorder)
    {
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task<Shortcut> TryAnswerAsync(TCommand message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _recorder.Calls.Enqueue(typeof(TCommand));
        return Task.FromResult(Shortcut.None);
    }
}
