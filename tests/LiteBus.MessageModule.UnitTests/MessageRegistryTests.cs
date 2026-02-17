using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;

namespace LiteBus.MessageModule.UnitTests;

[Collection("Sequential")]
public sealed class MessageRegistryTests : LiteBusTestBase
{
    // Test data types
    public enum CustomEnum
    {
        One,
        Two,
        Three
    }

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

    // --- Open Generic Handler Test Types ---

    public class TestCommand : ICommand;

    public class AnotherTestCommand : ICommand;

    public class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public Task HandleAsync(TestCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public class OpenGenericTestPreHandler<T> : ICommandPreHandler<T> where T : ICommand
    {
        public Task PreHandleAsync(T message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    // --- Open Generic Handler Tests ---

    [Fact]
    public void Register_OpenGenericHandler_ShouldLinkToExistingConcreteMessageType()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Register a concrete handler first (which also registers the message type)
        registry.Register(typeof(TestCommandHandler));

        // Act - register the open generic handler
        registry.Register(typeof(OpenGenericTestPreHandler<>));

        // Assert - the open generic should be closed for TestCommand
        var messageDescriptor = registry.Single(d => d.MessageType == typeof(TestCommand));
        messageDescriptor.PreHandlers.Should().HaveCount(1);
        messageDescriptor.PreHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<TestCommand>));
    }

    [Fact]
    public void Register_ConcreteMessageAfterOpenGenericHandler_ShouldLinkOpenGenericHandler()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Register the open generic handler first
        registry.Register(typeof(OpenGenericTestPreHandler<>));

        // Act - register a concrete handler (which also registers the message type)
        registry.Register(typeof(TestCommandHandler));

        // Assert - the open generic should be closed for TestCommand
        var messageDescriptor = registry.Single(d => d.MessageType == typeof(TestCommand));
        messageDescriptor.PreHandlers.Should().HaveCount(1);
        messageDescriptor.PreHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<TestCommand>));
    }

    [Fact]
    public void Register_OpenGenericHandler_ShouldApplyToMultipleConcreteMessageTypes()
    {
        // Arrange
        var registry = new MessageRegistry();

        registry.Register(typeof(TestCommandHandler));
        registry.Register(typeof(AnotherTestCommand));

        // Act
        registry.Register(typeof(OpenGenericTestPreHandler<>));

        // Assert
        var testCommandDescriptor = registry.Single(d => d.MessageType == typeof(TestCommand));
        testCommandDescriptor.PreHandlers.Should().HaveCount(1);
        testCommandDescriptor.PreHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<TestCommand>));

        var anotherCommandDescriptor = registry.Single(d => d.MessageType == typeof(AnotherTestCommand));
        anotherCommandDescriptor.PreHandlers.Should().HaveCount(1);
        anotherCommandDescriptor.PreHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<AnotherTestCommand>));
    }

    [Fact]
    public void Register_OpenGenericHandler_ShouldNotApplyToTypesNotSatisfyingConstraints()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Register a non-ICommand event type
        registry.Register(typeof(TestRecordClass));

        // Act - register an open generic handler constrained to ICommand
        registry.Register(typeof(OpenGenericTestPreHandler<>));

        // Assert - the event type should not have the command pre-handler
        var eventDescriptor = registry.Single(d => d.MessageType == typeof(TestRecordClass));
        eventDescriptor.PreHandlers.Should().BeEmpty();
    }

    [Fact]
    public void Register_OpenGenericHandlerTwice_ShouldOnlyRegisterOnce()
    {
        // Arrange
        var registry = new MessageRegistry();
        registry.Register(typeof(TestCommandHandler));

        // Act
        registry.Register(typeof(OpenGenericTestPreHandler<>));
        registry.Register(typeof(OpenGenericTestPreHandler<>));

        // Assert
        var messageDescriptor = registry.Single(d => d.MessageType == typeof(TestCommand));
        messageDescriptor.PreHandlers.Should().HaveCount(1);
    }

    [Fact]
    public void Clear_ShouldClearOpenGenericHandlers()
    {
        // Arrange
        var registry = new MessageRegistry();
        registry.Register(typeof(OpenGenericTestPreHandler<>));
        registry.Register(typeof(TestCommandHandler));

        // Verify it was registered
        registry.Single(d => d.MessageType == typeof(TestCommand)).PreHandlers.Should().HaveCount(1);

        // Act
        registry.Clear();

        // Register again without the open generic
        registry.Register(typeof(TestCommandHandler));

        // Assert - open generic should not be applied after Clear
        var descriptor = registry.Single(d => d.MessageType == typeof(TestCommand));
        descriptor.PreHandlers.Should().BeEmpty();
    }
}