using System.Text;
using Azure.Messaging.ServiceBus;
using LiteBus.Transport.Abstractions;
using LiteBus.Transport.AzureServiceBus;
using LiteBus.Transport.IntegrationTesting.Azure;

namespace LiteBus.Transport.IntegrationTests.Azure;

/// <summary>
///     Verifies the Azure Service Bus consumer runs handlers concurrently up to the lower of
///     <see cref="TransportConsumerOptions.MaxInFlightMessages" /> and
///     <see cref="TransportConsumerOptions.MaxConcurrentCalls" />, the one adapter with a bound above one.
/// </summary>
[Collection(ServiceBusEmulatorCollection.Name)]
[Trait("Category", TransportTestTraits.Azure)]
public sealed class AzureServiceBusMaxInFlightTests
{
    /// <summary>
    ///     The number of in-flight slots requested and messages published.
    /// </summary>
    private const int MaxInFlight = 8;

    /// <summary>
    ///     The shared Service Bus emulator fixture.
    /// </summary>
    private readonly ServiceBusEmulatorFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AzureServiceBusMaxInFlightTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared Service Bus emulator fixture.</param>
    public AzureServiceBusMaxInFlightTests(ServiceBusEmulatorFixture fixture)
    {
        _fixture = fixture;
        DockerTestGate.EnsureBrokerAvailable(_fixture.IsAvailable, "Azure Service Bus emulator");
        Skip.IfNot(_fixture.IsAvailable, DockerTestGate.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task StartAsync_WithMaxInFlightEight_ShouldRunEightHandlersConcurrently()
    {
        var queueName = ServiceBusEmulatorFixture.ResolveQueue("max-in-flight");
        var runId = Guid.NewGuid().ToString("N");
        var activeHandlers = 0;
        var peakHandlers = 0;
        var completedHandlers = 0;
        var releaseHandlers = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var client = new ServiceBusClient(_fixture.TransportOptions.ConnectionString);
        var publisher = new AzureServiceBusPublisher(client, new TransportCircuitBreakerRegistry());
        var consumer = new AzureServiceBusConsumer(client, _fixture.TransportOptions);

        try
        {
            await consumer.StartAsync(
                new TransportConsumerOptions
                {
                    Destination = queueName,
                    MaxInFlightMessages = MaxInFlight,
                    MaxConcurrentCalls = MaxInFlight
                },
                async (message, cancellationToken) =>
                {
                    // The queue is shared with other scenarios; settle anything left over without counting it.
                    if (Encoding.UTF8.GetString(message.Body.Span) != runId)
                    {
                        await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var current = Interlocked.Increment(ref activeHandlers);
                    UpdatePeak(ref peakHandlers, current);

                    try
                    {
                        await releaseHandlers.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref activeHandlers);
                    }

                    await message.AcceptAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref completedHandlers);
                }).ConfigureAwait(false);

            for (var i = 0; i < MaxInFlight; i++)
            {
                await publisher.PublishAsync(new TransportPublishRequest
                {
                    Destination = queueName,
                    Body = Encoding.UTF8.GetBytes(runId)
                }).ConfigureAwait(false);
            }

            await WaitForAsync(() => Volatile.Read(ref peakHandlers) >= MaxInFlight, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
            Volatile.Read(ref peakHandlers).Should().Be(MaxInFlight);

            releaseHandlers.TrySetResult();

            await WaitForAsync(() => Volatile.Read(ref completedHandlers) == MaxInFlight, TimeSpan.FromSeconds(30))
                .ConfigureAwait(false);
            Volatile.Read(ref completedHandlers).Should().Be(MaxInFlight);
            Volatile.Read(ref peakHandlers).Should().Be(MaxInFlight);
        }
        finally
        {
            releaseHandlers.TrySetResult();
            await consumer.StopAsync().ConfigureAwait(false);
            await consumer.DisposeAsync().ConfigureAwait(false);
            await publisher.DisposeAsync().ConfigureAwait(false);
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Waits until the supplied condition becomes true or the timeout elapses.
    /// </summary>
    /// <param name="condition">The condition polled until it returns <see langword="true" />.</param>
    /// <param name="timeout">The maximum time to wait.</param>
    /// <returns>A task that completes when the condition becomes true.</returns>
    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long) timeout.TotalMilliseconds;

        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Replaces a target with a larger candidate while preserving concurrent updates.
    /// </summary>
    /// <param name="target">The target updated atomically.</param>
    /// <param name="candidate">The candidate maximum.</param>
    private static void UpdatePeak(ref int target, int candidate)
    {
        var current = Volatile.Read(ref target);

        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref target, candidate, current);

            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}
