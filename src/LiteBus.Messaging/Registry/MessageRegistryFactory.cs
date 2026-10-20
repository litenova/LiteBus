using System.ComponentModel;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry;

/// <summary>
///     Creates an empty message registry without a module.
/// </summary>
/// <remarks>
///     <para>
///         A host gets its registry from <c>MessageModule</c>, which is where it belongs: one registry per module
///         configuration, shared by every messaging module. This exists for the callers that have no module graph, a
///         test harness running the shipped pipeline over hand-supplied handlers and a manual host composing LiteBus
///         by hand.
///     </para>
///     <para>
///         Nothing an application writes should need it. It is public because the registry implementation is internal
///         and those callers live in other assemblies.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class MessageRegistryFactory
{
    /// <summary>
    ///     Creates an empty registry.
    /// </summary>
    /// <returns>The registry, ready for <see cref="IMessageWriter.Register" /> calls.</returns>
    /// <remarks>
    ///     One instance per caller. The registry holds no process-wide state and is never reset, so a test that needs
    ///     isolation creates a new one rather than clearing an old one.
    /// </remarks>
    public static IMessageRegistry Create()
    {
        return new MessageRegistry();
    }
}
