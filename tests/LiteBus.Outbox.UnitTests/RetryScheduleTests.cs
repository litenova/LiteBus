using LiteBus.Messaging.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Covers the explicit retry ladder, which expresses a retry window that starts inside a minute and reaches days,
///     something no exponential curve under a single cap can express.
/// </summary>
public sealed class RetryScheduleTests
{
    [Fact]
    public void A_schedule_returns_each_delay_in_order()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Schedule,
            UseJitter = false,
            Delays =
            [
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(2)
            ]
        };

        options.CalculateDelay(1).Should().Be(TimeSpan.FromMinutes(1));
        options.CalculateDelay(2).Should().Be(TimeSpan.FromMinutes(5));
        options.CalculateDelay(3).Should().Be(TimeSpan.FromMinutes(30));
        options.CalculateDelay(4).Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void An_attempt_past_the_end_of_the_ladder_repeats_the_last_delay()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Schedule,
            UseJitter = false,
            Delays = [TimeSpan.FromMinutes(1), TimeSpan.FromDays(7)]
        };

        options.CalculateDelay(3).Should().Be(TimeSpan.FromDays(7));
        options.CalculateDelay(50).Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void MaxDelay_does_not_clamp_a_declared_ladder()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Schedule,
            UseJitter = false,
            MaxDelay = TimeSpan.FromMinutes(5),
            Delays = [TimeSpan.FromMinutes(1), TimeSpan.FromDays(7)]
        };

        // A cap that silently shortened a declared seven-day tail would defeat the reason for declaring it.
        options.CalculateDelay(2).Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void Jitter_stays_within_twenty_percent_of_the_ladder_entry()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Schedule,
            UseJitter = true,
            Delays = [TimeSpan.FromMinutes(10)]
        };

        for (var attempt = 0; attempt < 200; attempt++)
        {
            var delay = options.CalculateDelay(1);

            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(8));
            delay.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(12));
        }
    }

    [Fact]
    public void An_empty_ladder_is_refused_at_validation()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Schedule
        };

        var act = options.Validate;

        act.Should().Throw<InvalidOperationException>().WithMessage("*Delays is empty*");
    }

    [Fact]
    public void A_negative_ladder_entry_is_refused_at_validation()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Schedule,
            Delays = [TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(-1)]
        };

        var act = options.Validate;

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Delays[1]*");
    }

    [Fact]
    public void An_empty_ladder_is_ignored_by_every_other_backoff()
    {
        var options = new RetryOptions
        {
            Backoff = RetryBackoff.Exponential,
            InitialDelay = TimeSpan.FromSeconds(1),
            UseJitter = false
        };

        var act = options.Validate;

        act.Should().NotThrow();
        options.CalculateDelay(3).Should().Be(TimeSpan.FromSeconds(4));
    }
}
