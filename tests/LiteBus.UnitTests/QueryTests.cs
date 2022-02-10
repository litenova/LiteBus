using System.Threading.Tasks;
using FluentAssertions;
using LiteBus.Messaging.Extensions.MicrosoftDependencyInjection;
using LiteBus.Queries.Abstractions;
using LiteBus.Queries.Extensions.MicrosoftDependencyInjection;
using LiteBus.UnitTests.Data.FakeGenericQuery.Handlers;
using LiteBus.UnitTests.Data.FakeGenericQuery.Messages;
using LiteBus.UnitTests.Data.FakeGenericQuery.PostHandlers;
using LiteBus.UnitTests.Data.FakeGenericQuery.PreHandlers;
using LiteBus.UnitTests.Data.FakeQuery.Handlers;
using LiteBus.UnitTests.Data.FakeQuery.Messages;
using LiteBus.UnitTests.Data.FakeQuery.PostHandlers;
using LiteBus.UnitTests.Data.FakeQuery.PreHandlers;
using LiteBus.UnitTests.Data.Shared.QueryGlobalPostHandlers;
using LiteBus.UnitTests.Data.Shared.QueryGlobalPreHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LiteBus.UnitTests;

public class QueryTests
{
    [Fact]
    public async Task QueryAsync_FakeQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalQueryPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.RegisterPreHandler<FakeQueryPreHandler>();
                                      builder.RegisterHandler<FakeQueryHandler>();
                                      builder.RegisterPostHandler<FakeQueryPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new FakeQuery();

        // Act
        var queryResult = await queryMediator.QueryAsync(query);

        // Assert
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<FakeGlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<FakeQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<FakeQueryHandler>();
        query.ExecutedTypes[3].Should().Be<FakeQueryPostHandler>();
        query.ExecutedTypes[4].Should().Be<FakeGlobalQueryPostHandler>();
    }

    [Fact]
    public async Task QueryAsync_FakeGenericQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeGlobalQueryPreHandler>();
                                      builder.RegisterPostHandler<FakeGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.RegisterPreHandler(typeof(FakeGenericQueryPreHandler<>));
                                      builder.RegisterHandler(typeof(FakeGenericQueryHandler<>));
                                      builder.RegisterPostHandler(typeof(FakeGenericQueryPostHandler<>));
                                  });
                              })
                              .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new FakeGenericQuery<string>();

        // Act
        var queryResult = await queryMediator.QueryAsync(query);

        // Assert
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<FakeGlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<FakeGenericQueryPreHandler<string>>();
        query.ExecutedTypes[2].Should().Be<FakeGenericQueryHandler<string>>();
        query.ExecutedTypes[3].Should().Be<FakeGenericQueryPostHandler<string>>();
        query.ExecutedTypes[4].Should().Be<FakeGlobalQueryPostHandler>();
    }
    
        [Fact]
    public void Query_FakeQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeSyncGlobalQueryPreHandler>();
                                      builder.RegisterPostHandler<FakeSyncGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.RegisterPreHandler<FakeSyncQueryPreHandler>();
                                      builder.RegisterHandler<FakeSyncQueryHandler>();
                                      builder.RegisterPostHandler<FakeSyncQueryPostHandler>();
                                  });
                              })
                              .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new FakeQuery();

        // Act
        var queryResult = queryMediator.Query(query);

        // Assert
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<FakeSyncGlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<FakeSyncQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<FakeSyncQueryHandler>();
        query.ExecutedTypes[3].Should().Be<FakeSyncQueryPostHandler>();
        query.ExecutedTypes[4].Should().Be<FakeSyncGlobalQueryPostHandler>();
    }

    [Fact]
    public void Query_FakeGenericQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.RegisterPreHandler<FakeSyncGlobalQueryPreHandler>();
                                      builder.RegisterPostHandler<FakeSyncGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.RegisterPreHandler(typeof(FakeSyncGenericQueryPreHandler<>));
                                      builder.RegisterHandler(typeof(FakeSyncGenericQueryHandler<>));
                                      builder.RegisterPostHandler(typeof(FakeSyncGenericQueryPostHandler<>));
                                  });
                              })
                              .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new FakeGenericQuery<string>();

        // Act
        var queryResult = queryMediator.Query(query);

        // Assert
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<FakeSyncGlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<FakeSyncGenericQueryPreHandler<string>>();
        query.ExecutedTypes[2].Should().Be<FakeSyncGenericQueryHandler<string>>();
        query.ExecutedTypes[3].Should().Be<FakeSyncGenericQueryPostHandler<string>>();
        query.ExecutedTypes[4].Should().Be<FakeSyncGlobalQueryPostHandler>();
    }
}