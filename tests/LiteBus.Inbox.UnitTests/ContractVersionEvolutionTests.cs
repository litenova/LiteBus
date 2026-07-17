using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

/// <summary>
///     Verifies persisted contract versions resolve to their registered CLR shapes during inbox processing.
/// </summary>
public sealed class ContractVersionEvolutionTests
{
    /// <summary>
    ///     Verifies version 1 and version 2 payloads for one contract name deserialize and dispatch side by side.
    /// </summary>
    [Fact]
    public async Task ProcessPendingAsync_WithTwoContractVersions_ShouldDispatchEachRegisteredShape()
    {
        var recorder = new VersionedCommandRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(builder =>
            {
                builder.Register<CreateOrderV1>();
                builder.Register<CreateOrderV1Handler>();
                builder.Register<CreateOrderV2>();
                builder.Register<CreateOrderV2Handler>();
            });

            registry.AddInboxModule(builder =>
            {
                builder.Contracts.Register<CreateOrderV1>("orders.commands.create", 1);
                builder.Contracts.Register<CreateOrderV2>("orders.commands.create", 2);
                builder.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    DispatcherConcurrency = 2,
                    LeaseOwner = "contract-version-test"
                });
                builder.UseInMemoryStorage();
                builder.UseInProcessDispatch();
            });
        });

        await using var provider = services.BuildServiceProvider();
        var inbox = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();
        var store = provider.GetRequiredService<InMemoryInboxStore>();

        var version1 = await inbox.AcceptAsync(InboxAcceptItem<CreateOrderV1>.From(
            new CreateOrderV1 { OrderId = "order-v1" })).ConfigureAwait(false);
        var version2 = await inbox.AcceptAsync(InboxAcceptItem<CreateOrderV2>.From(
            new CreateOrderV2 { OrderId = "order-v2", Region = "eu-west" })).ConfigureAwait(false);

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        recorder.Entries.Should().BeEquivalentTo(
        [
            "v1:order-v1",
            "v2:order-v2:eu-west"
        ]);
        store.Get(version1.Id).Should().Match<InboxEnvelope>(envelope =>
            envelope.ContractName == "orders.commands.create" &&
            envelope.ContractVersion == 1 &&
            envelope.Status == InboxStatus.Completed);
        store.Get(version2.Id).Should().Match<InboxEnvelope>(envelope =>
            envelope.ContractName == "orders.commands.create" &&
            envelope.ContractVersion == 2 &&
            envelope.Status == InboxStatus.Completed);
    }

    private sealed record CreateOrderV1 : ICommand
    {
        public string OrderId { get; init; } = string.Empty;
    }

    private sealed record CreateOrderV2 : ICommand
    {
        public string OrderId { get; init; } = string.Empty;

        public string Region { get; init; } = string.Empty;
    }

    private sealed class CreateOrderV1Handler : ICommandHandler<CreateOrderV1>
    {
        private readonly VersionedCommandRecorder _recorder;

        public CreateOrderV1Handler(VersionedCommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(CreateOrderV1 message, CancellationToken cancellationToken = default)
        {
            _recorder.Record($"v1:{message.OrderId}");
            return Task.CompletedTask;
        }
    }

    private sealed class CreateOrderV2Handler : ICommandHandler<CreateOrderV2>
    {
        private readonly VersionedCommandRecorder _recorder;

        public CreateOrderV2Handler(VersionedCommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(CreateOrderV2 message, CancellationToken cancellationToken = default)
        {
            _recorder.Record($"v2:{message.OrderId}:{message.Region}");
            return Task.CompletedTask;
        }
    }

    private sealed class VersionedCommandRecorder
    {
        private readonly object _sync = new();

        private readonly List<string> _entries = [];

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_sync)
                {
                    return [.. _entries];
                }
            }
        }

        public void Record(string entry)
        {
            lock (_sync)
            {
                _entries.Add(entry);
            }
        }
    }
}
