using LiteBus.Events.Abstractions;

namespace LiteBus.Testing;

/// <summary>
///     Records events published through <see cref="IEventMediator" /> for test assertions.
/// </summary>
public sealed class FakeEventMediator : IEventMediator
{
    /// <summary>
    ///     Gets events recorded by <see cref="PublishAsync" /> overloads.
    /// </summary>
    private readonly List<object> _events = [];

    /// <summary>
    ///     Gets the events recorded since construction or the last <see cref="Clear" /> call.
    /// </summary>
    public IReadOnlyList<object> Events => _events;

    /// <inheritdoc />
    public Task PublishAsync(
        IEvent @event,
        EventMediationSettings? eventMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events.Add(@event);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PublishAsync<TEvent>(
        TEvent @event,
        EventMediationSettings? eventMediationSettings = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events.Add(@event);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Clears recorded events.
    /// </summary>
    public void Clear()
    {
        _events.Clear();
    }
}