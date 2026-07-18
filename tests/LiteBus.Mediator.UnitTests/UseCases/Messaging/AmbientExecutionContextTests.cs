using LiteBus.Messaging.Abstractions;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

/// <summary>
///     Unit tests for ambient execution context scoping and test reset helpers.
/// </summary>
public sealed class AmbientExecutionContextTests
{
    /// <summary>
    ///     Confirms disposing a scope restores the previous context and <see cref="AmbientExecutionContext.ResetForTesting" />
    ///     clears the ambient slot.
    /// </summary>
    [Fact]
    public void CreateScope_dispose_and_ResetForTesting_should_restore_and_clear_context()
    {
        AmbientExecutionContext.ResetForTesting();
        AmbientExecutionContext.HasCurrent.Should().BeFalse();

        var outer = new TestExecutionContext();

        using (AmbientExecutionContext.CreateScope(outer))
        {
            AmbientExecutionContext.Current.Should().BeSameAs(outer);

            var inner = new TestExecutionContext();

            using (AmbientExecutionContext.CreateScope(inner))
            {
                AmbientExecutionContext.Current.Should().BeSameAs(inner);
            }

            AmbientExecutionContext.Current.Should().BeSameAs(outer);
        }

        AmbientExecutionContext.HasCurrent.Should().BeFalse();

        AmbientExecutionContext.ResetForTesting();
        AmbientExecutionContext.HasCurrent.Should().BeFalse();
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public CancellationToken CancellationToken => CancellationToken.None;

        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        public IReadOnlyCollection<string> Tags { get; } = Array.Empty<string>();

        public object? MessageResult { get; set; }

        public void Abort(object? messageResult = null)
        {
            MessageResult = messageResult;
            throw new LiteBusExecutionAbortedException();
        }
    }
}