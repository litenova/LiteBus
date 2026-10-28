using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Testing;

/// <summary>
///     Builds a directly constructed outbox writer, scripted dispatcher, and pipelined processor over one manual clock.
/// </summary>
internal sealed class OutboxTestingHarness
{
    /// <summary>
    ///     Gets the frozen clock shared by the writer, store, and processor.
    /// </summary>
    internal ManualTimeProvider Clock { get; }

    /// <summary>
    ///     Gets the store shared by the writer and the processor.
    /// </summary>
    internal InMemoryOutboxStore Store { get; }

    /// <summary>
    ///     Gets the writer used to enqueue test events.
    /// </summary>
    internal Outbox Writer { get; }

    /// <summary>
    ///     Gets the scripted dispatcher observed and scripted by tests.
    /// </summary>
    internal ScriptedOutboxDispatcher Dispatcher { get; }

    /// <summary>
    ///     Gets the processor under test.
    /// </summary>
    internal PipelinedOutboxProcessor Processor { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxTestingHarness" /> class.
    /// </summary>
    /// <param name="retry">The retry settings applied after dispatch failures.</param>
    /// <param name="batchSize">The maximum number of messages leased per processing pass.</param>
    internal OutboxTestingHarness(RetryOptions retry, int batchSize = 10)
    {
        Clock = new ManualTimeProvider(new DateTimeOffset(2026, 6, 6, 8, 0, 0, TimeSpan.Zero));
        Store = new InMemoryOutboxStore(timeProvider: Clock);

        var contracts = new MessageContractRegistry();
        contracts.Register<OrderPlacedEvent>("orders.placed");
        contracts.Register<PaymentTakenEvent>("payments.taken");

        Writer = new Outbox(
            Store,
            new OutboxEnvelopeFactory(contracts, new SystemTextJsonMessageSerializer(), Clock));
        Dispatcher = new ScriptedOutboxDispatcher();
        Processor = new PipelinedOutboxProcessor(
            Store,
            Store,
            Dispatcher,
            new OutboxProcessorOptions
            {
                BatchSize = batchSize,
                LeaseOwner = "drain-test",
                Retry = retry
            },
            Clock,
            []);
    }

    /// <summary>
    ///     Builds a provider that resolves <see cref="IOutboxProcessor" /> to the harness processor.
    /// </summary>
    /// <returns>The configured service provider.</returns>
    internal ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxProcessor>(Processor);
        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     Sample integration event used by tests.
    /// </summary>
    internal sealed record OrderPlacedEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }

    /// <summary>
    ///     Sample integration event used by tests.
    /// </summary>
    internal sealed record PaymentTakenEvent
    {
        /// <summary>
        ///     Gets the order identifier.
        /// </summary>
        public Guid OrderId { get; init; }
    }
}
