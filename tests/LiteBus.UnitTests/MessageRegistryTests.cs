using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Internal.Registry;

namespace LiteBus.UnitTests;

public sealed class MessageRegistryTests
{
    [Fact]
    public void Register_RecordClass_ShouldRegisterAsMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(TestRecordClass));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(TestRecordClass));
    }

    [Fact]
    public void Register_RecordStruct_ShouldRegisterAsMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(TestRecordStruct));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(TestRecordStruct));
    }

    [Fact]
    public void Register_RegularClass_ShouldRegisterAsMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(TestClass));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(TestClass));
    }

    [Fact]
    public void Register_RegularStruct_ShouldRegisterAsMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(TestStruct));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(TestStruct));
    }

    [Fact]
    public void Register_CustomValueType_ShouldRegisterAsMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(CustomEnum));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(CustomEnum));
    }

    [Fact]
    public void Register_SystemType_ShouldNotRegisterAsMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(string));
        registry.Register(typeof(int));
        registry.Register(typeof(DateTime));

        // Assert
        registry.Should().BeEmpty();
    }

    [Fact]
    public void Register_SameTypeMultipleTimes_ShouldRegisterOnce()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(TestRecordClass));
        registry.Register(typeof(TestRecordClass));
        registry.Register(typeof(TestRecordClass));

        // Assert
        registry.Should().HaveCount(1);
    }

    [Fact]
    public void Register_GenericRecordStruct_ShouldRegisterGenericTypeDefinition()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(GenericRecordStruct<string>));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(GenericRecordStruct<>));
    }

    [Fact]
    public void Register_GenericRecordClass_ShouldRegisterGenericTypeDefinition()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(GenericRecordClass<int>));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(GenericRecordClass<>));
    }

    [Fact]
    public void Register_Handler_ShouldRegisterHandlerAndMessage()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(TestHandler));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(TestRecordStruct));

        // Handler should be registered with the message
        var messageDescriptor = registry.First();
        messageDescriptor.Handlers.Should().HaveCount(1);
        messageDescriptor.Handlers.First().HandlerType.Should().Be(typeof(TestHandler));
    }

    // Test data types
    public enum CustomEnum
    {
        One,
        Two,
        Three
    }

    public record TestRecordClass(string Name) : IEvent;

    public readonly record struct TestRecordStruct(string Name) : IEvent;

    public class TestClass : IEvent
    {
        public required string Name { get; set; }
    }

    public struct TestStruct
    {
        public string Name { get; set; }
    }

    public record GenericRecordClass<T>(T Value) : IEvent;

    public readonly record struct GenericRecordStruct<T>(T Value) : IEvent;

    public class TestHandler : IAsyncMessageHandler<TestRecordStruct>
    {
        public Task HandleAsync(TestRecordStruct message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}