using System;

namespace LiteBus.Saga.Storage.Marten;

/// <summary>
///     Configures the Marten saga store.
/// </summary>
/// <remarks>
///     <para>
///         Adjusted through the <c>configure</c> action on
///         <see cref="SagaModuleBuilderMartenExtensions.UseMarten(Saga.SagaModuleBuilder, Action{MartenSagaStoreOptions})" />.
///         The store resolves the application-owned Marten document store from the dependency registry, so the
///         connection and schema stay in the application Marten configuration and this type carries no settings
///         yet; it exists so later settings plug in without changing the registration shape.
///     </para>
/// </remarks>
public sealed class MartenSagaStoreOptions
{
}
