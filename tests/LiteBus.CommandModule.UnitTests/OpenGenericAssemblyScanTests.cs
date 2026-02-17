using LiteBus.CommandModule.UnitTests.UseCases;
using LiteBus.CommandModule.UnitTests.UseCases.OpenGenericAssemblyScan;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.CommandModule.UnitTests;

/// <summary>
///     Tests that verify <c>RegisterFromAssembly</c> automatically discovers open generic handlers
///     without requiring an explicit <c>Register(typeof(...))</c> call.
/// </summary>
[Collection("Sequential")]
public sealed class OpenGenericAssemblyScanTests : LiteBusTestBase
{
    [Fact]
    public async Task RegisterFromAssembly_DiscoversOpenGenericPreHandler_AndExecutesIt()
    {
        // Arrange — only RegisterFromAssembly, no explicit Register(typeof(...))
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ScanTestCommand).Assembly);
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new ScanTestCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert — open generic pre-handler should have been discovered and executed
        command.ExecutedTypes.Should().Contain(typeof(ScanTestOpenGenericPreHandler<ScanTestCommand>));
    }

    [Fact]
    public async Task RegisterFromAssembly_DiscoversOpenGenericPostHandler_AndExecutesIt()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ScanTestCommand).Assembly);
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new ScanTestCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert — open generic post-handler should have been discovered and executed
        command.ExecutedTypes.Should().Contain(typeof(ScanTestOpenGenericPostHandler<ScanTestCommand>));
    }

    [Fact]
    public async Task RegisterFromAssembly_OpenGenericHandlers_ExecuteInCorrectPipelineOrder()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ScanTestCommand).Assembly);
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();
        var command = new ScanTestCommand();

        // Act
        await commandMediator.SendAsync(command);

        // Assert — pre-handler runs before main handler, post-handler runs after
        var preIndex = command.ExecutedTypes.IndexOf(typeof(ScanTestOpenGenericPreHandler<ScanTestCommand>));
        var mainIndex = command.ExecutedTypes.IndexOf(typeof(ScanTestCommandHandler));
        var postIndex = command.ExecutedTypes.IndexOf(typeof(ScanTestOpenGenericPostHandler<ScanTestCommand>));

        preIndex.Should().BeGreaterThanOrEqualTo(0, "pre-handler should have executed");
        mainIndex.Should().BeGreaterThanOrEqualTo(0, "main handler should have executed");
        postIndex.Should().BeGreaterThanOrEqualTo(0, "post-handler should have executed");

        preIndex.Should().BeLessThan(mainIndex, "pre-handler should run before main handler");
        mainIndex.Should().BeLessThan(postIndex, "main handler should run before post-handler");
    }

    [Fact]
    public async Task RegisterFromAssembly_OpenGenericHandlers_ApplyToMultipleCommandTypes()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ScanTestCommand).Assembly);
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command1 = new ScanTestCommand();
        var command2 = new AnotherScanTestCommand();

        // Act
        await commandMediator.SendAsync(command1);
        await commandMediator.SendAsync(command2);

        // Assert — open generic handlers should be closed for both command types
        command1.ExecutedTypes.Should().Contain(typeof(ScanTestOpenGenericPreHandler<ScanTestCommand>));
        command1.ExecutedTypes.Should().Contain(typeof(ScanTestOpenGenericPostHandler<ScanTestCommand>));

        command2.ExecutedTypes.Should().Contain(typeof(ScanTestOpenGenericPreHandler<AnotherScanTestCommand>));
        command2.ExecutedTypes.Should().Contain(typeof(ScanTestOpenGenericPostHandler<AnotherScanTestCommand>));
    }

    [Fact]
    public async Task RegisterFromAssembly_OpenGenericHandlers_RespectConstraints_DoNotApplyToUnrelatedCommands()
    {
        // Arrange — CreateProductCommand does NOT implement IOpenGenericScanTestCommand
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ScanTestCommand).Assembly);
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command = new UseCases.CreateProduct.CreateProductCommand();

        // Act
        var result = await commandMediator.SendAsync(command);

        // Assert — CreateProductCommand should NOT have the constrained open generic handlers.
        // We verify by checking that none of the executed types are closed forms of the scan-test open generics.
        result.Should().NotBeNull();
        command.ExecutedTypes
            .Where(t => t.IsGenericType)
            .Select(t => t.GetGenericTypeDefinition())
            .Should().NotContain(typeof(ScanTestOpenGenericPreHandler<>));
        command.ExecutedTypes
            .Where(t => t.IsGenericType)
            .Select(t => t.GetGenericTypeDefinition())
            .Should().NotContain(typeof(ScanTestOpenGenericPostHandler<>));
    }

    [Fact]
    public async Task RegisterFromAssembly_DiscoversOpenGenericHandlers_WithoutExplicitRegistration()
    {
        // Arrange — ONLY RegisterFromAssembly, no Register(typeof(...)) at all.
        // This proves that assembly scanning is sufficient to discover open generic handlers.
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.RegisterFromAssembly(typeof(ScanTestCommand).Assembly);
                });
            })
            .BuildServiceProvider();

        var commandMediator = serviceProvider.GetRequiredService<ICommandMediator>();

        var command1 = new ScanTestCommand();
        var command2 = new AnotherScanTestCommand();

        // Act
        await commandMediator.SendAsync(command1);
        await commandMediator.SendAsync(command2);

        // Assert — both commands should have the full pipeline:
        // open generic pre-handler → main handler → open generic post-handler
        var command1Relevant = command1.ExecutedTypes
            .Where(t => t == typeof(ScanTestCommandHandler)
                     || (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ScanTestOpenGenericPreHandler<>)
                                          || t.GetGenericTypeDefinition() == typeof(ScanTestOpenGenericPostHandler<>))))
            .ToList();

        command1Relevant.Should().HaveCount(3);
        command1Relevant[0].Should().Be(typeof(ScanTestOpenGenericPreHandler<ScanTestCommand>));
        command1Relevant[1].Should().Be(typeof(ScanTestCommandHandler));
        command1Relevant[2].Should().Be(typeof(ScanTestOpenGenericPostHandler<ScanTestCommand>));

        var command2Relevant = command2.ExecutedTypes
            .Where(t => t == typeof(AnotherScanTestCommandHandler)
                     || (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ScanTestOpenGenericPreHandler<>)
                                          || t.GetGenericTypeDefinition() == typeof(ScanTestOpenGenericPostHandler<>))))
            .ToList();

        command2Relevant.Should().HaveCount(3);
        command2Relevant[0].Should().Be(typeof(ScanTestOpenGenericPreHandler<AnotherScanTestCommand>));
        command2Relevant[1].Should().Be(typeof(AnotherScanTestCommandHandler));
        command2Relevant[2].Should().Be(typeof(ScanTestOpenGenericPostHandler<AnotherScanTestCommand>));
    }
}
