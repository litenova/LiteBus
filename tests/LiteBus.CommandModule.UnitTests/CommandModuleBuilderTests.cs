using System.Reflection;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;

namespace LiteBus.CommandModule.UnitTests;

public sealed class CommandModuleBuilderTests : LiteBusTestBase
{
    [Fact]
    public void RegisterFromAssembly_WithNullAssembly_ThrowsArgumentNullException()
    {
        var builder = new CommandModuleBuilder(MessageRegistryAccessor.Instance);

        var act = () => builder.RegisterFromAssembly(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterFromAssembly_DoesNotRegisterMarkerInterfaces()
    {
        MessageRegistryAccessor.Instance.Clear();

        var builder = new CommandModuleBuilder(MessageRegistryAccessor.Instance);
        builder.RegisterFromAssembly(typeof(ICommand).Assembly);

        MessageRegistryAccessor.Instance
            .Should()
            .NotContain(descriptor => descriptor.MessageType == typeof(ICommand));
    }
}
