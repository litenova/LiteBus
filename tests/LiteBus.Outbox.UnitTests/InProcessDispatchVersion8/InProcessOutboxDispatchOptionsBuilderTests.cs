using LiteBus.Events;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Outbox;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.InProcessDispatchVersion8;

/// <summary>
///     Verifies the in-process outbox dispatch acknowledgement for envelopes without a handler.
/// </summary>
public sealed class InProcessOutboxDispatchOptionsBuilderTests
{
    /// <summary>
    ///     Verifies default composition requires a handler and records no reason.
    /// </summary>
    [Fact]
    public void UseInProcessDispatch_by_default_should_require_handler()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    outbox.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<InProcessOutboxDispatchOptions>();

        options.RequireHandler.Should().BeTrue();
        options.MissingHandlerReason.Should().BeNull();
    }

    /// <summary>
    ///     Verifies the acknowledgement records the reason and allows envelopes without a handler.
    /// </summary>
    [Fact]
    public void UseInProcessDispatch_with_allowance_should_record_reason()
    {
        const string reason = "Single-node leaf service with no local subscribers yet.";

        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    outbox.UseInMemoryStorage();
                    outbox.UseInProcessDispatch(dispatch => dispatch.AllowEnvelopesWithoutHandler(reason));
                });
            })
            .BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<InProcessOutboxDispatchOptions>();

        options.RequireHandler.Should().BeFalse();
        options.MissingHandlerReason.Should().Be(reason);
    }

    /// <summary>
    ///     Verifies a null acknowledgement reason is rejected.
    /// </summary>
    [Fact]
    public void AllowEnvelopesWithoutHandler_with_null_reason_should_throw()
    {
        var builder = new InProcessOutboxDispatchOptionsBuilder();

        var act = () => builder.AllowEnvelopesWithoutHandler(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Verifies a blank acknowledgement reason is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AllowEnvelopesWithoutHandler_with_blank_reason_should_throw(string reason)
    {
        var builder = new InProcessOutboxDispatchOptionsBuilder();

        var act = () => builder.AllowEnvelopesWithoutHandler(reason);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    ///     Verifies the acknowledgement returns the builder for chaining.
    /// </summary>
    [Fact]
    public void AllowEnvelopesWithoutHandler_should_return_same_builder()
    {
        var builder = new InProcessOutboxDispatchOptionsBuilder();

        var result = builder.AllowEnvelopesWithoutHandler("Documented allowance for a subscriber-free deployment.");

        result.Should().BeSameAs(builder);
    }
}
