using LiteBus.Commands.Abstractions;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Registry;
using LiteBus.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Messaging;

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
    public void Register_ClosedGenericRecordStruct_ShouldPreserveExactType()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(GenericRecordStruct<string>));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(GenericRecordStruct<string>));
    }

    [Fact]
    public void Register_ClosedGenericRecordClass_ShouldPreserveExactType()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        registry.Register(typeof(GenericRecordClass<int>));

        // Assert
        registry.Should().HaveCount(1);
        registry.First().MessageType.Should().Be(typeof(GenericRecordClass<int>));
    }

    [Fact]
    public void Register_ClosedGenericHandlers_ShouldKeepIndependentDescriptors()
    {
        var registry = new MessageRegistry();

        registry.Register(typeof(ClosedGenericStringHandler));
        registry.Register(typeof(ClosedGenericIntHandler));

        var stringDescriptor = registry.Find(typeof(ClosedGenericCommand<string>));
        var intDescriptor = registry.Find(typeof(ClosedGenericCommand<int>));

        stringDescriptor.Should().NotBeNull();
        stringDescriptor!.MessageType.Should().Be(typeof(ClosedGenericCommand<string>));
        stringDescriptor.Handlers.Should().ContainSingle()
            .Which.HandlerType.Should().Be(typeof(ClosedGenericStringHandler));

        intDescriptor.Should().NotBeNull();
        intDescriptor!.MessageType.Should().Be(typeof(ClosedGenericCommand<int>));
        intDescriptor.Handlers.Should().ContainSingle()
            .Which.HandlerType.Should().Be(typeof(ClosedGenericIntHandler));
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
        messageDescriptor.PreStageHandlers.Should().HaveCount(1);
        messageDescriptor.PreStageHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<TestCommand>));
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
        messageDescriptor.PreStageHandlers.Should().HaveCount(1);
        messageDescriptor.PreStageHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<TestCommand>));
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
        testCommandDescriptor.PreStageHandlers.Should().HaveCount(1);
        testCommandDescriptor.PreStageHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<TestCommand>));

        var anotherCommandDescriptor = registry.Single(d => d.MessageType == typeof(AnotherTestCommand));
        anotherCommandDescriptor.PreStageHandlers.Should().HaveCount(1);
        anotherCommandDescriptor.PreStageHandlers.First().HandlerType.Should().Be(typeof(OpenGenericTestPreHandler<AnotherTestCommand>));
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
        eventDescriptor.PreStageHandlers.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_OpenGenericHandler_ShouldApplySubstitutedSelfReferentialConstraints(
        bool registerHandlerFirst)
    {
        var registry = new MessageRegistry();

        if (registerHandlerFirst)
        {
            registry.Register(typeof(SelfComparablePreHandler<>));
            registry.Register(typeof(SelfComparableCommand));
        }
        else
        {
            registry.Register(typeof(SelfComparableCommand));
            registry.Register(typeof(SelfComparablePreHandler<>));
        }

        var descriptor = registry.Single(item => item.MessageType == typeof(SelfComparableCommand));

        descriptor.PreStageHandlers.Should().ContainSingle()
            .Which.HandlerType.Should().Be(typeof(SelfComparablePreHandler<SelfComparableCommand>));
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
        messageDescriptor.PreStageHandlers.Should().HaveCount(1);
    }

    [Fact]
    public void Find_ExactType_ShouldReturnDescriptorInConstantTime()
    {
        // Arrange
        var registry = new MessageRegistry();
        registry.Register(typeof(TestCommandHandler));

        // Act
        var descriptor = registry.Find(typeof(TestCommand));

        // Assert
        descriptor.Should().NotBeNull();
        descriptor!.MessageType.Should().Be(typeof(TestCommand));
    }

    [Fact]
    public void NewRegistryInstance_ShouldNotRetainOpenGenericHandlersFromAnotherInstance()
    {
        // Arrange
        var firstRegistry = new MessageRegistry();
        firstRegistry.Register(typeof(OpenGenericTestPreHandler<>));
        firstRegistry.Register(typeof(TestCommandHandler));
        firstRegistry.Single(d => d.MessageType == typeof(TestCommand)).PreStageHandlers.Should().HaveCount(1);

        var secondRegistry = new MessageRegistry();

        // Act
        secondRegistry.Register(typeof(TestCommandHandler));

        // Assert
        var descriptor = secondRegistry.Single(d => d.MessageType == typeof(TestCommand));
        descriptor.PreStageHandlers.Should().BeEmpty();
    }

    [Fact]
    public void Register_OpenGenericHandlerWithMultipleGenericParameters_ShouldThrowUnsupportedOpenGenericHandlerException()
    {
        // Arrange
        var registry = new MessageRegistry();

        // Act
        var act = () => registry.Register(typeof(UnsupportedOpenGenericTestPreHandler<,>));

        // Assert
        var exception = act.Should().Throw<UnsupportedOpenGenericHandlerException>();
        exception.Which.HandlerType.Should().Be(typeof(UnsupportedOpenGenericTestPreHandler<,>));
        exception.Which.GenericParameterCount.Should().Be(2);
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

    public sealed record ClosedGenericCommand<T>(T Value) : ICommand;

    public sealed class ClosedGenericStringHandler : ICommandHandler<ClosedGenericCommand<string>>
    {
        public Task HandleAsync(ClosedGenericCommand<string> message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class ClosedGenericIntHandler : ICommandHandler<ClosedGenericCommand<int>>
    {
        public Task HandleAsync(ClosedGenericCommand<int> message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

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

    public class UnsupportedOpenGenericTestPreHandler<TCommand, TContext> : ICommandPreHandler<TCommand> where TCommand : ICommand
    {
        public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class SelfComparableCommand : ICommand, IComparable<SelfComparableCommand>
    {
        public int CompareTo(SelfComparableCommand? other)
        {
            return other is null ? 1 : 0;
        }
    }

    private sealed class SelfComparablePreHandler<TCommand> : ICommandPreHandler<TCommand>
        where TCommand : ICommand, IComparable<TCommand>
    {
        public Task PreHandleAsync(TCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
