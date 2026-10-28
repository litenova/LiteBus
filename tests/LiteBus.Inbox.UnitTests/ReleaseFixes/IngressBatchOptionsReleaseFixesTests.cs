using LiteBus.Runtime.Abstractions.Exceptions;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies batch accept rejects a zero maximum wait that would stall partial batches.
/// </summary>
public sealed class IngressBatchOptionsReleaseFixesTests
{
    /// <summary>
    ///     Verifies batch accept with a zero maximum wait fails validation instead of stalling.
    /// </summary>
    [Fact]
    public void Validate_WhenBatchAcceptEnabledWithZeroMaxWait_ShouldThrow()
    {
        var options = new TransportInboxIngressSafetyOptions
        {
            EnableBatchAccept = true,
            BatchMaxWait = TimeSpan.Zero
        };

        var act = options.Validate;

        act.Should().Throw<DurableStorageConfigurationException>()
            .WithMessage("*BatchMaxWait*");
    }

    /// <summary>
    ///     Verifies batch accept with a positive maximum wait passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenBatchAcceptEnabledWithPositiveMaxWait_ShouldNotThrow()
    {
        var options = new TransportInboxIngressSafetyOptions
        {
            EnableBatchAccept = true,
            BatchMaxWait = TimeSpan.FromMilliseconds(200)
        };

        var act = options.Validate;

        act.Should().NotThrow();
    }

    /// <summary>
    ///     Verifies a zero maximum wait without batch accept still passes validation.
    /// </summary>
    [Fact]
    public void Validate_WhenBatchAcceptDisabledWithZeroMaxWait_ShouldNotThrow()
    {
        var options = new TransportInboxIngressSafetyOptions
        {
            BatchMaxWait = TimeSpan.Zero
        };

        var act = options.Validate;

        act.Should().NotThrow();
    }
}
