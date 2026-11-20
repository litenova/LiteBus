using System.Text.Json;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Reads the handler list of the composition model from a real host, as an architecture rule would: which handler
///     serves which message, in which stage, with which subscriber name, from which assembly.
/// </summary>
internal static class CompositionHandlersSmoke
{
    /// <summary>
    ///     The subscriber name the scenario's event handler declares.
    /// </summary>
    internal const string SubscriberName = "smoke.composition.order-placed";

    /// <summary>
    ///     Runs the scenario. It needs no database.
    /// </summary>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(liteBus =>
        {
            liteBus.AddMessaging(_ =>
            {
            });
            liteBus.AddCommands(commands => commands
                .Register<CompositionSmokeCommand>()
                .Register<CompositionSmokeCommandHandler>()
                .Register<CompositionSmokeCommandPostHandler>());
            liteBus.AddEvents(events => events
                .Register<CompositionSmokeEvent>()
                .Register<CompositionSmokeEventHandler>());
        });

        await using var provider = services.BuildServiceProvider();
        var model = provider.GetRequiredService<ICompositionModel>();

        var eventHandler = model.Handlers.SingleOrDefault(entry => entry.HandlerType == typeof(CompositionSmokeEventHandler));
        Smoke.Expect(eventHandler is not null, "the composition model lists the event handler");
        Smoke.Expect(eventHandler!.MessageType == typeof(CompositionSmokeEvent), "the event handler serves its event");
        Smoke.Expect(eventHandler.SubscriberName == SubscriberName, "the event handler entry carries its subscriber name");
        Smoke.Expect(eventHandler.Stage == "handler", "the event handler runs in the handler stage");
        Smoke.Expect(
            eventHandler.Assembly == typeof(CompositionHandlersSmoke).Assembly.GetName().Name,
            "the entry names the assembly that defines the handler");

        var postHandler = model.Handlers.SingleOrDefault(entry => entry.HandlerType == typeof(CompositionSmokeCommandPostHandler));
        Smoke.Expect(postHandler?.Stage == "post", "the post-handler runs in the post stage");
        Smoke.Expect(postHandler?.SubscriberName is null, "a handler that declares no subscriber name reports none");

        using var json = JsonDocument.Parse(model.ToJson());
        Smoke.Expect(
            json.RootElement.GetProperty("handlers").EnumerateArray()
                .Any(entry => entry.GetProperty("subscriberName").GetString() == SubscriberName),
            "the composition snapshot includes the handler list");
    }
}

/// <summary>
///     A command the scenario registers.
/// </summary>
internal sealed record CompositionSmokeCommand : ICommand;

/// <summary>
///     Handles the scenario's command.
/// </summary>
internal sealed class CompositionSmokeCommandHandler : ICommandHandler<CompositionSmokeCommand>
{
    /// <inheritdoc />
    public Task HandleAsync(CompositionSmokeCommand message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     Runs after the scenario's command.
/// </summary>
internal sealed class CompositionSmokeCommandPostHandler : ICommandPostHandler<CompositionSmokeCommand>
{
    /// <inheritdoc />
    public Task PostHandleAsync(
        CompositionSmokeCommand message,
        object? messageResult,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
///     An event the scenario registers.
/// </summary>
internal sealed record CompositionSmokeEvent : IEvent;

/// <summary>
///     Handles the scenario's event under a stable subscriber name.
/// </summary>
[SubscriberName(CompositionHandlersSmoke.SubscriberName)]
internal sealed class CompositionSmokeEventHandler : IEventHandler<CompositionSmokeEvent>
{
    /// <inheritdoc />
    public Task HandleAsync(CompositionSmokeEvent message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
