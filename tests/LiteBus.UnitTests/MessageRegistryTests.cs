using System.Collections;
using LiteBus.Messaging.Internal.Registry;
using LiteBus.UnitTests.Data.FakeCommand.Messages;

namespace LiteBus.UnitTests;

public sealed class MessageRegistryTests
{
    [Fact]
    public void Register_SimpleType_ShouldRegisterAsAMessage()
    {
        // Arrange
        var messageRegistry = new MessageRegistry();

        // Act
        messageRegistry.Register(typeof(FakeCommand));
        messageRegistry.Register(typeof(FakeCommand));
        messageRegistry.Register(typeof(int));

        // Assert
        messageRegistry.Should().HaveCount(1);
        messageRegistry.Single().MessageType.Should().Be(typeof(FakeCommand));
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(IEnumerable))]
    [InlineData(typeof(DateTime))]
    public void Register_ShouldNotRegister(Type type)
    {
        // Arrange
        var messageRegistry = new MessageRegistry();

        // Act
        messageRegistry.Register(type);

        // Assert
        messageRegistry.Should().BeEmpty();
    }
}