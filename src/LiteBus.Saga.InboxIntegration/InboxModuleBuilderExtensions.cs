using LiteBus.Inbox.Abstractions;

namespace LiteBus.Saga;

/// <summary>
///     Provides saga configuration extensions for <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderExtensions
{
    /// <summary>
    ///     Enables inbox saga support and registers the saga orchestration processor hook.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">An optional callback that maps saga state types to contract names.</param>
    /// <returns>The current builder.</returns>
    public static InboxModuleBuilder EnableSaga(
        this InboxModuleBuilder builder,
        Action<SagaModuleBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.RegisterSaga(new SagaModule(configure ?? (_ => { })));
    }
}
