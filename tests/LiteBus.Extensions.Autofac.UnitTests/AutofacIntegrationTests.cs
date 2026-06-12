using Autofac;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Autofac.UnitTests.UseCases;
using LiteBus.Inbox;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Runtime.Abstractions.Hosting;
using LiteBus.Testing;

namespace LiteBus.Extensions.Autofac.UnitTests;

/// <summary>
///     Contains tests to verify the LiteBus integration with an Autofac container.
///     These tests must run sequentially because they rely on the static MessageRegistry.
/// </summary>
[Collection("Sequential")]
public sealed class AutofacIntegrationTests : LiteBusTestBase
{
    [Fact]
    public async Task AddLiteBus_WithCommandModule_ResolvesAndExecutesHandlersCorrectly()
    {
        // ARRANGE
        var builder = new ContainerBuilder();

        // Configure LiteBus using the Autofac extension
        builder.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddCommandModule(commandModuleBuilder =>
            {
                commandModuleBuilder.Register<RegisterComponentCommand>();
                commandModuleBuilder.Register<RegisterComponentCommandPreHandler>();
                commandModuleBuilder.Register<RegisterComponentCommandHandler>();
                commandModuleBuilder.Register<RegisterComponentCommandPostHandler>();
            });
        });

        var container = builder.Build();
        var commandMediator = container.Resolve<ICommandMediator>();
        var command = new RegisterComponentCommand();

        // ACT
        await commandMediator.SendAsync(command).ConfigureAwait(false);

        // ASSERT
        // Verify that all handlers were resolved from the Autofac container and executed in the correct order.
        command.ExecutedHandlers.Should().HaveCount(3);
        command.ExecutedHandlers[0].Should().Be<RegisterComponentCommandPreHandler>();
        command.ExecutedHandlers[1].Should().Be<RegisterComponentCommandHandler>();
        command.ExecutedHandlers[2].Should().Be<RegisterComponentCommandPostHandler>();
    }

    [Fact]
    public void AddLiteBus_WithModuleRegistryOverload_ShouldRegisterLiteBusHostManifest()
    {
        var builder = new ContainerBuilder();

        builder.AddLiteBus(registry =>
        {
            registry.AddMessageModule(_ =>
            {
            });

            registry.AddInboxModule(inbox =>
            {
                inbox.UseInMemoryStorage();
                inbox.AddDiagnosticCheck<SampleDiagnosticCheck>("litebus.sample");
            });
        });

        using var container = builder.Build();
        var manifest = container.Resolve<LiteBusHostManifest>();

        manifest.DiagnosticChecks.Should().ContainSingle();
        manifest.DiagnosticChecks[0].Name.Should().Be("litebus.sample");
    }

    private sealed class SampleDiagnosticCheck : IDiagnosticCheck
    {
        public string Name => "litebus.sample";

        public Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DiagnosticResult(DiagnosticStatus.Healthy, "Sample probe succeeded."));
        }
    }
}