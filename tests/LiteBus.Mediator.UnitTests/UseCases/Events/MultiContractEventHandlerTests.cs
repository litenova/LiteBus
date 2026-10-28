using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Events;

/// <summary>
///     Verifies that one class implementing several closed event handler contracts compiles without a hand-written
///     untyped entry point and dispatches each event to the contract its registration names.
/// </summary>
/// <remarks>
///     The handler class below implements two closed <see cref="IEventHandler{TEvent}" /> contracts and nothing else. It
///     compiling at all is the first half of the regression: while <c>IMessageHandler</c> declared an untyped
///     <c>Handle(object)</c> with a default implementation in every closed contract, such a class failed with CS8705.
/// </remarks>
public sealed class MultiContractEventHandlerTests
{
    [Fact]
    public async Task each_event_reaches_its_own_contract_through_the_generic_overload()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var recorder = serviceProvider.GetRequiredService<MultiContractRecorder>();

        await eventMediator.PublishAsync(new RefundSucceeded(), CancellationToken.None).ConfigureAwait(false);
        await eventMediator.PublishAsync(new RefundSettledOutside(), CancellationToken.None).ConfigureAwait(false);

        recorder.Handled.Should().Equal(typeof(RefundSucceeded), typeof(RefundSettledOutside));
    }

    [Fact]
    public async Task each_event_reaches_its_own_contract_when_published_as_IEvent()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var recorder = serviceProvider.GetRequiredService<MultiContractRecorder>();

        await eventMediator.PublishAsync((IEvent) new RefundSettledOutside()).ConfigureAwait(false);
        await eventMediator.PublishAsync((IEvent) new RefundSucceeded()).ConfigureAwait(false);

        recorder.Handled.Should().Equal(typeof(RefundSettledOutside), typeof(RefundSucceeded));
    }

    [Fact]
    public async Task an_erased_publish_passes_the_mediation_cancellation_token_to_the_registered_contract()
    {
        await using var serviceProvider = BuildServiceProvider();
        var eventMediator = serviceProvider.GetRequiredService<IEventMediator>();
        var recorder = serviceProvider.GetRequiredService<MultiContractRecorder>();
        using var cancellation = new CancellationTokenSource();

        await eventMediator.PublishAsync((IEvent) new RefundSucceeded(), cancellation.Token).ConfigureAwait(false);

        recorder.Tokens.Should().ContainSingle().Which.Should().Be(cancellation.Token);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        return new ServiceCollection()
            .AddSingleton<MultiContractRecorder>()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder => builder.Register<RefundInventoryReaction>());
            })
            .BuildServiceProvider();
    }

    private sealed class RefundSucceeded : IEvent;

    private sealed class RefundSettledOutside : IEvent;

    private sealed class MultiContractRecorder
    {
        public List<Type> Handled { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];
    }

    private sealed class RefundInventoryReaction : IEventHandler<RefundSucceeded>, IEventHandler<RefundSettledOutside>
    {
        private readonly MultiContractRecorder _recorder;

        public RefundInventoryReaction(MultiContractRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(RefundSucceeded message, CancellationToken cancellationToken = default)
        {
            _recorder.Handled.Add(typeof(RefundSucceeded));
            _recorder.Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task HandleAsync(RefundSettledOutside message, CancellationToken cancellationToken = default)
        {
            _recorder.Handled.Add(typeof(RefundSettledOutside));
            return Task.CompletedTask;
        }
    }
}
