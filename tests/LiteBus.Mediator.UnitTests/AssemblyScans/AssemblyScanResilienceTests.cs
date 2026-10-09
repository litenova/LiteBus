using System.Reflection;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.AssemblyScans;

public sealed class AssemblyScanResilienceTests : LiteBusTestBase
{
    [Fact]
    public void GetLoadableTypes_WithNullAssembly_ThrowsArgumentNullException()
    {
        var act = () => AssemblyTypeScanner.GetLoadableTypes(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetLoadableTypes_WithPartiallyUnloadableAssembly_ReturnsLoadableTypes()
    {
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientCommand), null);

        var types = AssemblyTypeScanner.GetLoadableTypes(assembly);

        types.Should().ContainSingle().Which.Should().Be(typeof(ScanResilientCommand));
    }

    [Fact]
    public void GetLoadableTypes_WithHealthyAssembly_ReturnsAllTypes()
    {
        var types = AssemblyTypeScanner.GetLoadableTypes(typeof(object).Assembly);

        types.Should().Contain(typeof(object));
    }

    [Fact]
    public void MessageContractBuilder_AddFromAssembly_WithUnloadableType_RegistersLoadableContracts()
    {
        var builder = new MessageContractBuilder();
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientContractMessage), null);

        var act = () => builder.AddFromAssembly(assembly);

        act.Should().NotThrow();
        builder.HasRegistrations.Should().BeTrue();
    }

    [Fact]
    public void MessageContractRegistry_AddFromAssembly_WithUnloadableType_RegistersLoadableContracts()
    {
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientContractMessage), null);
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                messaging.Contracts.AddFromAssembly(assembly);
            });

            registry.AddCommands(_ =>
            {
            });
        }).BuildServiceProvider();

        var contracts = serviceProvider.GetRequiredService<IMessageContractRegistry>();

        contracts.TryGetMessageType("scan-resilience-contract", 1).Should().Be(typeof(ScanResilientContractMessage));
    }

    [Fact]
    public void CommandModule_RegisterFromAssembly_WithUnloadableType_RegistersLoadableCommands()
    {
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientCommand), null);
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.RegisterFromAssembly(assembly);
            });
        }).BuildServiceProvider();

        var messageRegistry = serviceProvider.GetRequiredService<IMessageRegistry>();

        messageRegistry.Should().Contain(descriptor => descriptor.MessageType == typeof(ScanResilientCommand));
    }

    [Fact]
    public void MessageModule_RegisterFromAssembly_WithUnloadableType_RegistersLoadableMessages()
    {
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientCommand), null);
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging =>
            {
                messaging.RegisterFromAssembly(assembly);
            });

            registry.AddCommands(_ =>
            {
            });
        }).BuildServiceProvider();

        var messageRegistry = serviceProvider.GetRequiredService<IMessageRegistry>();

        messageRegistry.Should().Contain(descriptor => descriptor.MessageType == typeof(ScanResilientCommand));
    }

    [Fact]
    public void EventModule_RegisterFromAssembly_WithUnloadableType_RegistersLoadableEvents()
    {
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientEvent), null);
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(events =>
            {
                events.RegisterFromAssembly(assembly);
            });
        }).BuildServiceProvider();

        var messageRegistry = serviceProvider.GetRequiredService<IMessageRegistry>();

        messageRegistry.Should().Contain(descriptor => descriptor.MessageType == typeof(ScanResilientEvent));
    }

    [Fact]
    public void QueryModule_RegisterFromAssembly_WithUnloadableType_RegistersLoadableQueries()
    {
        var assembly = new PartiallyUnloadableAssembly(typeof(ScanResilientQuery), null);
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(queries =>
            {
                queries.RegisterFromAssembly(assembly);
            });
        }).BuildServiceProvider();

        var messageRegistry = serviceProvider.GetRequiredService<IMessageRegistry>();

        messageRegistry.Should().Contain(descriptor => descriptor.MessageType == typeof(ScanResilientQuery));
    }

    [MessageContract("scan-resilience-contract", 1)]
    private sealed class ScanResilientContractMessage
    {
    }

    private sealed class ScanResilientCommand : ICommand
    {
    }

    private sealed class ScanResilientEvent : IEvent
    {
    }

    private sealed class ScanResilientQuery : IQuery
    {
    }

    private sealed class PartiallyUnloadableAssembly : Assembly
    {
        private readonly Type?[] _types;

        public PartiallyUnloadableAssembly(params Type?[] types)
        {
            _types = types;
        }

        public override Type[] GetTypes()
        {
            var loaderExceptions = new Exception?[_types.Length];

            for (var index = 0; index < _types.Length; index++)
            {
                if (_types[index] is null)
                {
                    loaderExceptions[index] = new FileLoadException("Simulated unloadable type.");
                }
            }

            throw new ReflectionTypeLoadException(_types, loaderExceptions);
        }
    }
}
