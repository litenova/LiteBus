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
    public async Task Send_FakeQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.Register<FakeGlobalQueryPreHandler>();
                                      builder.Register<FakeGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.Register<FakeQueryPreHandler>();
                                      builder.Register<FakeQueryHandlerWithoutResult>();
                                      builder.Register<FakeQueryPostHandler>();
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
        query.ExecutedTypes[2].Should().Be<FakeQueryHandlerWithoutResult>();
        query.ExecutedTypes[3].Should().Be<FakeQueryPostHandler>();
        query.ExecutedTypes[4].Should().Be<FakeGlobalQueryPostHandler>();
    }

    [Fact]
    public async Task Send_FakeGenericQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection()
                              .AddLiteBus(configuration =>
                              {
                                  configuration.AddQueries(builder =>
                                  {
                                      // Global Handlers
                                      builder.Register<FakeGlobalQueryPreHandler>();
                                      builder.Register<FakeGlobalQueryPostHandler>();

                                      // Fake Query Handlers
                                      builder.Register(typeof(FakeGenericQueryPreHandler<>));
                                      builder.Register(typeof(FakeGenericQueryHandlerWithoutResult<>));
                                      builder.Register(typeof(FakeGenericQueryPostHandler<>));
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
        query.ExecutedTypes[2].Should().Be<FakeGenericQueryHandlerWithoutResult<string>>();
        query.ExecutedTypes[3].Should().Be<FakeGenericQueryPostHandler<string>>();
        query.ExecutedTypes[4].Should().Be<FakeGlobalQueryPostHandler>();
    }
}