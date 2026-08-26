using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProduct;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProductByCriteria;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.IndirectStreamProducts;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.NoHandlerStream;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.ProblematicQuery;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.QueryWithTag;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamErrorHandling;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.StreamProducts;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries;

public sealed class QueryModuleTests : LiteBusTestBase
{
    [Fact]
    public async Task Mediating_GetProductQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new GetProductQuery();

        // Act
        var queryResult = await queryMediator.QueryAsync(query).ConfigureAwait(true);

        // Assert
        queryResult.Should().NotBeNull();
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(6);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<GetProductQueryHandlerPreHandler>();
        query.ExecutedTypes[2].Should().Be<GetProductQueryHandler>();
        query.ExecutedTypes[3].Should().Be<GetProductQueryHandlerPostHandler1>();
        query.ExecutedTypes[4].Should().Be<GetProductQueryHandlerPostHandler2>();
        query.ExecutedTypes[5].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task Mediating_GetProductByCriteriaQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var queryPayload = new PriceCriteria { Min = 1, Max = 100 };
        var query = new GetProductByCriteriaQuery<PriceCriteria> { Payload = queryPayload };

        // Act
        var queryResult = await queryMediator.QueryAsync(query).ConfigureAwait(true);

        // Assert
        queryResult.Should().NotBeNull();
        queryResult.CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<GetProductByCriteriaQueryPreHandler<PriceCriteria>>();
        query.ExecutedTypes[2].Should().Be<GetProductByCriteriaQueryHandler<PriceCriteria>>();
        query.ExecutedTypes[3].Should().Be<GetProductByCriteriaQueryPostHandler<PriceCriteria>>();
        query.ExecutedTypes[4].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task Mediating_StreamProductsQuery_ShouldGoThroughHandlersCorrectly()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
                builder.Register<StreamProductsQuery>();
                builder.Register<StreamProductsQueryHandler>();
                builder.Register<StreamProductsQueryHandlerPostHandler2>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery();

        // Act
        var queryResult = await queryMediator.StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // Assert
        queryResult.First().CorrelationId.Should().Be(query.CorrelationId);
        query.ExecutedTypes.Should().HaveCount(6);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<StreamProductsQueryHandlerPreHandler>();
        query.ExecutedTypes[2].Should().Be<StreamProductsQueryHandler>();
        query.ExecutedTypes[3].Should().Be<StreamProductsQueryHandlerPostHandler1>();
        query.ExecutedTypes[4].Should().Be<StreamProductsQueryHandlerPostHandler2>();
        query.ExecutedTypes[5].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task mediating_a_query_with_exception_in_pre_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new ProblematicQuery { ThrowExceptionInType = typeof(ProblematicQueryPreHandler) };

        // Act
        await queryMediator.QueryAsync(query).ConfigureAwait(true);

        // Assert
        query.ExecutedTypes.Should().HaveCount(5);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<ProblematicQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<GlobalQueryErrorHandler>();
        query.ExecutedTypes[3].Should().Be<ProblematicQueryErrorHandler>();
        query.ExecutedTypes[4].Should().Be<ProblematicQueryErrorHandler2>();
    }

    [Fact]
    public async Task mediating_a_query_with_exception_in_post_global_handler_goes_through_error_handlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new ProblematicQuery { ThrowExceptionInType = typeof(GlobalQueryPostHandler) };

        // Act
        await queryMediator.QueryAsync(query).ConfigureAwait(true);

        // Assert
        query.ExecutedTypes.Should().HaveCount(8);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<ProblematicQueryPreHandler>();
        query.ExecutedTypes[2].Should().Be<ProblematicQueryHandler>();
        query.ExecutedTypes[3].Should().Be<ProblematicQueryPostHandler>();
        query.ExecutedTypes[4].Should().Be<GlobalQueryPostHandler>();
        query.ExecutedTypes[5].Should().Be<GlobalQueryErrorHandler>();
        query.ExecutedTypes[6].Should().Be<ProblematicQueryErrorHandler>();
        query.ExecutedTypes[7].Should().Be<ProblematicQueryErrorHandler2>();
    }

    [Fact]
    public async Task mediating_an_query_with_specified_tag_goes_through_handlers_with_that_tag_and_handlers_without_any_tag_correctly()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new QueryWithTag();
        var settings = new QueryMediationSettings { Routing = new QueryRoutingSettings { Tags = [Tags.Tag1] } };

        // Act
        await queryMediator.QueryAsync(query, settings).ConfigureAwait(true);

        // Assert
        query.ExecutedTypes.Should().HaveCount(7);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<QueryWithTagPreHandler1>();
        query.ExecutedTypes[2].Should().Be<QueryWithTagPreHandler3>();
        query.ExecutedTypes[3].Should().Be<QueryWithTagPreHandler4>();
        query.ExecutedTypes[4].Should().Be<QueryWithTagHandler1>();
        query.ExecutedTypes[5].Should().Be<QueryWithTagPostHandler1>();
        query.ExecutedTypes[6].Should().Be<GlobalQueryPostHandler>();
    }

    [Fact]
    public async Task mediating_the_an_query_with_both_all_available_tags_will_fail_as_there_are_two_main_handlers()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new QueryWithTag();
        var settings = new QueryMediationSettings { Routing = new QueryRoutingSettings { Tags = [Tags.Tag1, Tags.Tag2] } };

        // Act
        Func<Task> act = async () => await queryMediator.QueryAsync(query, settings).ConfigureAwait(true);

        // Assert
        await act.Should().ThrowAsync<MultipleHandlerFoundException>();
    }

    [Fact]
    public async Task mediating_a_stream_query_that_is_short_circuited_by_a_gate_goes_through_correct_handlers()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery { ShortCircuitInGate = true };

        // Act
        var queryResult = await queryMediator.StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // Assert
        queryResult.Should().BeEmpty();
        query.ExecutedTypes.Should().HaveCount(2);
        query.ExecutedTypes[0].Should().Be<GlobalQueryPreHandler>();
        query.ExecutedTypes[1].Should().Be<StreamProductsQueryHandlerPreHandler>();
    }

    [Fact]
    public async Task Mediating_StreamQuery_PassesMetadataViaExecutionContext()
    {
        // Arrange
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.Register<StreamProductsQueryHandler>();
                builder.Register<StreamProductsQueryHandlerPostHandler1>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamProductsQuery();

        // Act
        await queryMediator.StreamAsync(query).ToListAsync().ConfigureAwait(true);

        // Assert
        // The post-handler should have retrieved the count from the execution context
        // and set it on the query object.
        query.RetrievedStreamCount.Should().Be(1);
    }

    [Fact]
    public async Task Mediating_StreamQuery_WithIndirectHandler_ShouldUseBaseTypeHandler()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.Register<IndirectStreamProductsQuery>();
                builder.Register<IndirectStreamProductsQueryHandler>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new IndirectStreamProductsQuery();

        var results = await queryMediator.StreamAsync(query).ToListAsync().ConfigureAwait(true);

        results.Should().ContainSingle();

        query.ExecutedTypes.Should().ContainSingle()
            .Which.Should().Be<IndirectStreamProductsQueryHandler>();
    }

    [Fact]
    public async Task Mediating_StreamQuery_WithNoHandler_ShouldThrowNoHandlerFoundException()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.Register<EmptyStreamQuery>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new EmptyStreamQuery();

        var act = async () => await queryMediator.StreamAsync(query).ToListAsync().ConfigureAwait(true);

        var exception = await act.Should().ThrowAsync<NoHandlerFoundException>();
        exception.Which.Message.Should().Contain("RegisterFromAssembly");
    }

    [Fact]
    public async Task Mediating_StreamQuery_ErrorHandler_ReceivesMessageResultNotExceptionDispatchInfo()
    {
        var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddQueries(builder =>
            {
                builder.RegisterFromQueriesTestAssembly();
                builder.Register<StreamErrorHandlingQuery>();
                builder.Register<StreamErrorHandlingQueryHandler>();
            });
        }).BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();
        var query = new StreamErrorHandlingQuery();

        var results = await queryMediator.StreamAsync(query).ToListAsync().ConfigureAwait(true);

        query.ExecutedTypes.Should().Contain(typeof(StreamErrorHandlingQueryErrorHandler));
        query.ObservedErrorHandlerMessageResult.Should().NotBeNull();
        query.ObservedErrorHandlerMessageResult.Should().BeAssignableTo<IAsyncEnumerable<StreamErrorHandlingQueryResult>>();
        results.Should().HaveCount(1);
    }
}
