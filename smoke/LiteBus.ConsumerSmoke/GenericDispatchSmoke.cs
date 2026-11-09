using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.ConsumerSmoke;

/// <summary>
///     Sends generic messages to handlers registered by their open definitions, the shapes from #169.
/// </summary>
/// <remarks>
///     Each handler's type parameters sit somewhere other than the message's only type argument: nested in the result,
///     spread over two parameters, nested in a streamed item, or in an interface a non-generic event implements. The
///     mediator has to close the handler and its contract from the runtime message for every one of them.
/// </remarks>
internal static class GenericDispatchSmoke
{
    /// <summary>
    ///     Runs the generic dispatch scenario.
    /// </summary>
    /// <returns>A task that completes when the scenario has passed.</returns>
    internal static async Task RunAsync()
    {
        var recorder = new SmokeRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        await using var provider = services
            .AddLiteBus(liteBus =>
            {
                liteBus.AddMessaging(_ => { });
                liteBus.AddQueries(queries =>
                {
                    queries.Register(typeof(SmokeListQueryHandler<>));
                    queries.Register(typeof(SmokePagedQueryHandler<>));
                });
                liteBus.AddCommands(commands =>
                {
                    commands.Register(typeof(SmokeCreateEntityCommandHandler<,>));
                    commands.Register(typeof(SmokeCreateEntityCommandPostHandler<,>));
                });
                liteBus.AddEvents(events =>
                {
                    events.Register<SmokeCustomerChangedEvent>();
                    events.Register(typeof(SmokeEntityChangedEventHandler<>));
                });
            })
            .BuildServiceProvider();

        var list = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new SmokeListQuery<string>("value")).ConfigureAwait(false);
        Smoke.Expect(list is ["value"], "a query whose result nests the type parameter reaches its handler");

        var customer = new SmokeCustomer(Guid.NewGuid());
        var id = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new SmokeCreateEntityCommand<SmokeCustomer, Guid> { EntityData = customer }).ConfigureAwait(false);
        Smoke.Expect(id == customer.Id, "a command with two type parameters reaches its handler");

        var pages = new List<int>();

        await foreach (var page in provider.GetRequiredService<IQueryMediator>()
                           .StreamAsync(new SmokePagedQuery<string>("item", 2)).ConfigureAwait(false))
        {
            pages.Add(page.Number);
        }

        Smoke.Expect(pages is [1, 2], "a stream query whose items nest the type parameter streams through its handler");

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new SmokeCustomerChangedEvent(customer)).ConfigureAwait(false);

        Smoke.Expect(
            recorder.Entries.SequenceEqual([$"post:SmokeCustomer:{customer.Id}", "changed:SmokeCustomer"]),
            "a typed post-handler and a handler for a generic event interface run, " +
            $"recorded: {string.Join(", ", recorder.Entries)}");
    }
}
