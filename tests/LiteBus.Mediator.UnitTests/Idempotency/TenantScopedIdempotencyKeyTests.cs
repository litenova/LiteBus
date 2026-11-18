using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using LiteBus.Testing.Mediation;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Idempotency;

/// <summary>
///     Verifies idempotency keys scoped by the execution identity tenant: isolation across tenants, the
///     slash-prefixed stored key, the unprefixed key for a mediation without a tenant, and that a trace tenant
///     without an identity does not scope the key.
/// </summary>
[Collection("Sequential")]
public sealed class TenantScopedIdempotencyKeyTests : LiteBusTestBase
{
    [Fact]
    public async Task The_same_key_under_different_execution_identities_runs_for_each_tenant()
    {
        var applications = new ApplicationCounter();
        var provider = BuildProvider(
            new InMemoryIdempotencyStore(),
            applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var tenantA = new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant("tenant-a") };
        var tenantB = new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant("tenant-b") };

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantA).ConfigureAwait(false);
        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantB).ConfigureAwait(false);

        applications.Count.Should().Be(2);

        var repeat = await mediator
            .TrySendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantA).ConfigureAwait(false);

        repeat.Outcome.Should().Be(MediationOutcome.Answered);
        applications.Count.Should().Be(2);
    }

    [Fact]
    public async Task A_tenant_scoped_claim_is_stored_with_the_tenant_and_a_slash_prefix()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var provider = BuildProvider(store, applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var tenantA = new CommandMediationSettings { Identity = ExecutionIdentity.ForTenant("tenant-a") };

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantA).ConfigureAwait(false);

        store.AppliedKeys.Count.Should().Be(1);
        store.AppliedKeys.Should().Contain($"tenant-a/{typeof(ApplyPaymentCommand).FullName}:order-1");
    }

    [Fact]
    public async Task A_mediation_without_a_tenant_stores_the_key_without_a_prefix()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var provider = BuildProvider(store, applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }).ConfigureAwait(false);

        store.AppliedKeys.Count.Should().Be(1);
        store.AppliedKeys.Should().Contain($"{typeof(ApplyPaymentCommand).FullName}:order-1");
    }

    [Fact]
    public async Task A_trace_tenant_without_an_execution_identity_scopes_the_key()
    {
        var applications = new ApplicationCounter();
        var store = new InMemoryIdempotencyStore();
        var provider = BuildProvider(store, applications);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        var tenantA = new CommandMediationSettings();
        tenantA.Items[MessageTraceContextKeys.TenantId] = "tenant-a";
        var tenantB = new CommandMediationSettings();
        tenantB.Items[MessageTraceContextKeys.TenantId] = "tenant-b";

        await mediator.SendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantA).ConfigureAwait(false);

        var repeat = await mediator
            .TrySendAsync(new ApplyPaymentCommand { PaymentId = "order-1" }, tenantB).ConfigureAwait(false);

        repeat.Outcome.Should().Be(MediationOutcome.Succeeded);
        applications.Count.Should().Be(2);
        store.AppliedKeys.Count.Should().Be(2);
        store.AppliedKeys.Should().Contain($"tenant-a/{typeof(ApplyPaymentCommand).FullName}:order-1");
        store.AppliedKeys.Should().Contain($"tenant-b/{typeof(ApplyPaymentCommand).FullName}:order-1");
    }

    /// <summary>
    ///     Builds a provider with idempotency enabled and the given store registered.
    /// </summary>
    /// <param name="store">The store shared with the test.</param>
    /// <param name="applications">The counter recording how many times each handler actually ran.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(
        IIdempotencyStore store,
        ApplicationCounter applications)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddSingleton(applications);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<ApplyPaymentCommand>();
                    builder.Register<ApplyPaymentCommandHandler>();
                    builder.Register<ApplyPaymentCommandDefinition>();
                    builder.EnableIdempotency();
                });
            })
            .BuildServiceProvider();
    }
}
