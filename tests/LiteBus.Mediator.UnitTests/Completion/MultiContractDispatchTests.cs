using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Verifies that one class can implement pipeline contracts for several message types, and that each dispatch
///     reaches the contract the handler was registered under.
/// </summary>
[Collection("Sequential")]
public sealed class MultiContractDispatchTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering the multi-contract handler and both probe commands.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider()
    {
        return new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(ProbeCommandA));
                    builder.Register(typeof(ProbeCommandB));
                    builder.Register(typeof(ProbeCommandAHandler));
                    builder.Register(typeof(ProbeCommandBHandler));
                    builder.Register(typeof(MultiContractHandler));
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Each_message_type_reaches_its_own_contract_on_a_shared_handler()
    {
        var provider = BuildProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var commandA = new ProbeCommandA();
        await mediator.SendAsync(commandA).ConfigureAwait(false);

        commandA.Ran.Should().Equal("pre:A", "main:A", "post:A", "done:A");
    }

    [Fact]
    public async Task A_result_returning_message_reaches_the_typed_post_handler_contract()
    {
        var provider = BuildProvider();
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var commandB = new ProbeCommandB();
        var result = await mediator.SendAsync(commandB).ConfigureAwait(false);

        result.Should().Be("done");

        // The typed post-handler received the result rather than the untyped overload being chosen by name.
        commandB.Ran.Should().Equal("pre:B", "main:B", "post:B:done", "done:B:done");
    }
}
