namespace LiteBus.Testing;

/// <summary>
///     Entry point for building a <see cref="MediationHarness{TMessage}" />.
/// </summary>
/// <remarks>
///     A separate non-generic type so the message type is named once, at the call site, rather than repeated in a
///     constructor.
/// </remarks>
public static class MediationHarness
{
    /// <summary>
    ///     Starts a harness for one message type.
    /// </summary>
    /// <typeparam name="TMessage">The message type under test.</typeparam>
    /// <returns>The harness, ready for handlers.</returns>
    public static MediationHarness<TMessage> For<TMessage>()
        where TMessage : notnull
    {
        return new MediationHarness<TMessage>();
    }
}
