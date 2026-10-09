using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.Registry;

/// <summary>
///     Verifies that enumerating the handler snapshot stays consistent while another thread commits registrations.
/// </summary>
public sealed class MessageRegistryConcurrencyTests : LiteBusTestBase
{
    [Fact]
    public async Task Handlers_WhenEnumeratedDuringConcurrentCommits_ReturnsSelfConsistentSnapshots()
    {
        var registry = new MessageRegistry();
        using var writerFinished = new ManualResetEventSlim(false);

        var reader = Task.Run(() =>
        {
            while (!writerFinished.IsSet)
            {
                var snapshot = registry.Handlers;
                var enumerated = 0;

                foreach (var _ in snapshot)
                {
                    enumerated++;
                }

                snapshot.Count.Should().Be(enumerated);
            }
        });

        try
        {
            // Each marker nests one level deeper, so every iteration commits a distinct handler.
            var marker = typeof(int);

            for (var i = 0; i < 200; i++)
            {
                registry.Register(typeof(ConcurrencyTestHandler<>).MakeGenericType(marker));
                marker = typeof(List<>).MakeGenericType(marker);
            }
        }
        finally
        {
            writerFinished.Set();
        }

        await reader.ConfigureAwait(false);

        registry.Handlers.Should().HaveCount(200);
    }

    private sealed class ConcurrencyTestMessage<TMarker>
        where TMarker : notnull
    {
    }

    private sealed class ConcurrencyTestHandler<TMarker> : IMessageHandler<ConcurrencyTestMessage<TMarker>, string>
        where TMarker : notnull
    {
        public string Handle(ConcurrencyTestMessage<TMarker> message)
        {
            return "handled";
        }
    }
}
