using System.Runtime.CompilerServices;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Tenancy;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers the composition check for recurring tenant-required commands: a registration without an identity fails
///     naming the command and the registration, while an identity factory, a tenant sweep, or per-item batch
///     identities satisfy it.
/// </summary>
public sealed class RecurringTenantRequiredCompositionTests
{
    [Fact]
    public void Recurring_without_identity_for_a_tenant_required_command_fails_naming_both_types()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<TenantJobCommandHandler>()
                .AddRecurring(
                    () => new TenantJobCommand(),
                    host => host.Interval = TimeSpan.FromMilliseconds(5)));
        });

        act.Should().Throw<LiteBusConfigurationException>().WithMessage(
            $"*{typeof(TenantJobCommand).FullName}*{typeof(RecurringCommandModule<TenantJobCommand>).FullName}*");
    }

    [Fact]
    public void Recurring_with_identity_for_a_tenant_required_command_composes()
    {
        using var provider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<TenantJobCommandHandler>()
                .AddRecurring(
                    () => new TenantJobCommand(),
                    host => host.Interval = TimeSpan.FromMilliseconds(5),
                    identity: _ => ExecutionIdentity.ForTenant("acme")));
        }).BuildServiceProvider();

        provider.GetRequiredService<RecurringCommandBackgroundService<TenantJobCommand>>().Should().NotBeNull();
    }

    [Fact]
    public void Sweep_for_a_tenant_required_command_composes()
    {
        using var provider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<TenantJobCommandHandler>()
                .AddRecurringForEachTenant(
                    tenant => new TenantJobCommand { Tenant = tenant },
                    new ScriptedTenantSource(["acme"]),
                    host => host.Interval = TimeSpan.FromMilliseconds(5)));
        }).BuildServiceProvider();

        provider.GetRequiredService<RecurringTenantSweepBackgroundService<TenantJobCommand>>().Should().NotBeNull();
    }

    [Fact]
    public void Recurring_without_identity_for_an_undeclared_command_composes()
    {
        using var provider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<PlainJobCommandHandler>()
                .AddRecurring(
                    () => new PlainJobCommand(),
                    host => host.Interval = TimeSpan.FromMilliseconds(5)));
        }).BuildServiceProvider();

        provider.GetRequiredService<RecurringCommandBackgroundService<PlainJobCommand>>().Should().NotBeNull();
    }

    [Fact]
    public void Batch_for_a_tenant_required_command_composes_with_per_item_identities()
    {
        // The batch's commands are known only at runtime, so composition cannot check them: the source sets the
        // identity on each item instead.
        using var provider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<TenantJobCommandHandler>()
                .AddRecurringBatch<TenantJobBatchSource>(
                    host => host.Interval = TimeSpan.FromMilliseconds(5)));
        }).BuildServiceProvider();

        provider.GetRequiredService<RecurringCommandBatchBackgroundService<TenantJobBatchSource>>().Should().NotBeNull();
    }

    /// <summary>
    ///     A command that requires a tenant, declared with an attribute.
    /// </summary>
    [TenantRequired]
    public sealed class TenantJobCommand : ICommand
    {
        public string Tenant { get; init; } = "";
    }

    /// <summary>
    ///     A command declaring no tenancy position.
    /// </summary>
    public sealed class PlainJobCommand : ICommand;

    private sealed class TenantJobCommandHandler : ICommandHandler<TenantJobCommand>
    {
        public Task HandleAsync(TenantJobCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class PlainJobCommandHandler : ICommandHandler<PlainJobCommand>
    {
        public Task HandleAsync(PlainJobCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TenantJobBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
                [new RecurringCommandItem(new TenantJobCommand(), ExecutionIdentity.ForTenant("acme"))],
                isolation: RecurringBatchIsolation.PerItem));
        }
    }

    private sealed class ScriptedTenantSource : IRecurringTenantSource
    {
        private readonly string[] _tenants;

        public ScriptedTenantSource(string[] tenants)
        {
            _tenants = tenants;
        }

        public async IAsyncEnumerable<string> GetTenantsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var tenant in _tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return tenant;
            }
        }
    }
}
