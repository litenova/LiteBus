using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Saga.Abstractions;
using LiteBus.Saga.InboxIntegration;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies saga contract mappings fail composition when the definition has no registered state type.
/// </summary>
public sealed class SagaContractMappingReleaseFixesTests
{
    /// <summary>
    ///     Verifies a contract mapped to an unknown saga definition fails module build.
    /// </summary>
    [Fact]
    public void BuildServiceProvider_WhenContractMapsToUnknownDefinition_ShouldThrow()
    {
        var act = () =>
        {
            new ServiceCollection()
                .AddLiteBus(registry =>
                {
                    registry.AddMessaging(_ =>
                    {
                    });

                    registry.AddInbox(inbox =>
                    {
                        inbox.UseInMemoryStorage();
                        inbox.EnableSaga(saga =>
                        {
                            saga.DefineState<OrderState>("order");
                            saga.MapContract("order.placed", "orders");
                            saga.UseInMemoryStorage();
                        });
                    });
                })
                .BuildServiceProvider();
        };

        act.Should().Throw<DurableStorageConfigurationException>()
            .WithMessage("*order.placed*orders*");
    }

    /// <summary>
    ///     Verifies a contract mapped to a registered saga definition builds and resolves.
    /// </summary>
    /// <returns>A task that completes when the registration assertion succeeds.</returns>
    [Fact]
    public async Task BuildServiceProvider_WhenContractMapsToKnownDefinition_ShouldResolveRegistry()
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddInbox(inbox =>
                {
                    inbox.UseInMemoryStorage();
                    inbox.EnableSaga(saga =>
                    {
                        saga.DefineState<OrderState>("order");
                        saga.MapContract("order.placed", "order");
                        saga.UseInMemoryStorage();
                    });
                });
            })
            .BuildServiceProvider();

        await using (provider.ConfigureAwait(false))
        {
            var registry = provider.GetRequiredService<ISagaStateTypeRegistry>();

            registry.ResolveDefinitionId("order.placed").Should().Be("order");
            registry.ResolveStateType("order").Should().Be<OrderState>();
        }
    }

    /// <summary>
    ///     Saga state type used by contract mapping release tests.
    /// </summary>
    private sealed class OrderState
    {
        /// <summary>
        ///     Gets or sets the workflow step.
        /// </summary>
        public int Step { get; set; }
    }
}
