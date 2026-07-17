using LiteBus.Inbox.Abstractions;

namespace LiteBus.Saga.InboxIntegration;

/// <summary>
///     Provides saga configuration extensions for <see cref="InboxModuleBuilder" />.
/// </summary>
public static class InboxModuleBuilderSagaExtensions
{
    /// <summary>
    ///     Enables inbox saga support and registers the saga orchestration processor hook.
    /// </summary>
    /// <param name="builder">The inbox module builder.</param>
    /// <param name="configure">The callback that registers state mappings and selects one saga store.</param>
    /// <returns>The current builder.</returns>
    public static InboxModuleBuilder EnableSaga(
        this InboxModuleBuilder builder,
        Action<SagaModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.RegisterSaga(new SagaModule(configure));

        return builder.RegisterSaga(new SagaInboxCommandScopeModule());
    }
}
