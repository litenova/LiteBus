using LiteBus.Commands;
using LiteBus.Messaging;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.HandledAt;

/// <summary>
///     Verifies that the execution context carries the instant its mediation started, read once from the configured
///     clock.
/// </summary>
[Collection("Sequential")]
public sealed class HandledAtTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider whose messaging module reads the handled instant from the given clock.
    /// </summary>
    /// <param name="clock">The clock the module and the handlers share.</param>
    /// <param name="capture">The capture shared with the handlers.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(StubTimeProvider clock, HandledAtCapture capture)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(capture);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(messaging =>
                {
                    messaging.UseTimeProvider(clock);
                });

                registry.AddCommands(builder =>
                {
                    builder.Register<HandledAtCommand>();
                    builder.Register<HandledAtCommandHandler>();
                    builder.Register<HandledAtNestedCommand>();
                    builder.Register<HandledAtNestedCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task HandledAt_is_read_once_when_the_mediation_starts()
    {
        var start = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var clock = new StubTimeProvider { UtcNow = start };
        var capture = new HandledAtCapture();
        var mediator = BuildProvider(clock, capture).GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new HandledAtCommand { AdvanceClock = true }).ConfigureAwait(false);

        // The clock moved mid-mediation, but the context kept the instant it read when it was created.
        capture.Observed.Select(o => o.Name).Should().Equal("outer", "outer-after-advance");
        capture.Observed.Select(o => o.HandledAt).Should().Equal(start, start);
    }

    [Fact]
    public async Task Nested_mediation_reads_its_own_instant()
    {
        var start = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var clock = new StubTimeProvider { UtcNow = start };
        var capture = new HandledAtCapture();
        var mediator = BuildProvider(clock, capture).GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new HandledAtCommand { SendNested = true }).ConfigureAwait(false);

        var observed = capture.Observed.ToList();

        observed.Should().HaveCount(2);
        observed[0].Should().Be(("outer", start));
        observed[1].Should().Be(("inner", start.AddHours(1)));
    }

    [Fact]
    public async Task Each_mediation_reads_a_fresh_instant()
    {
        var first = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
        var second = first.AddDays(1);
        var clock = new StubTimeProvider { UtcNow = first };
        var capture = new HandledAtCapture();
        var mediator = BuildProvider(clock, capture).GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new HandledAtCommand()).ConfigureAwait(false);

        clock.UtcNow = second;

        await mediator.SendAsync(new HandledAtCommand()).ConfigureAwait(false);

        capture.Observed.Select(o => o.HandledAt).Should().Equal(first, second);
    }
}
