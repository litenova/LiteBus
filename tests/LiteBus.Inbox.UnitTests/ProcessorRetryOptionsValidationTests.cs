using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies shared durable processor retry option validation.
/// </summary>
public sealed class ProcessorRetryOptionsValidationTests
{
    /// <summary>
    ///     Verifies that processor construction rejects retry values that could produce invalid visibility timestamps.
    /// </summary>
    /// <param name="scenario">The invalid retry configuration scenario to verify.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Constructor_ShouldRejectInvalidRetryConfiguration(int scenario)
    {
        var retry = scenario switch
        {
            0 => new RetryOptions { InitialDelay = TimeSpan.FromMilliseconds(-1) },
            1 => new RetryOptions { MaxDelay = TimeSpan.FromMilliseconds(-1) },
            2 => new RetryOptions { Backoff = (RetryBackoff) 42 },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown retry validation scenario.")
        };

        var store = new InMemoryInboxStore();

        var act = () => new PipelinedInboxProcessor(
            store,
            store,
            new InboxTestFixtures.StubInboxDispatcher(),
            new InboxProcessorOptions { Retry = retry },
            TimeProvider.System,
            []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
