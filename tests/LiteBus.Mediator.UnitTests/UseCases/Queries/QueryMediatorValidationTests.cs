using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Mediator.UnitTests.UseCases.Queries.UseCases.GetProduct;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Queries;

public sealed class QueryMediatorValidationTests : LiteBusTestBase
{
    [Fact]
    public async Task QueryAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddQueries(builder =>
                {
                    builder.RegisterFromQueriesTestAssembly();
                });
            })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();

        var act = async () => await queryMediator.QueryAsync<GetProductQueryResult>(null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task StreamAsync_WithNullQuery_ThrowsArgumentNullException()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddQueries(builder =>
                {
                    builder.RegisterFromQueriesTestAssembly();
                });
            })
            .BuildServiceProvider();

        var queryMediator = serviceProvider.GetRequiredService<IQueryMediator>();

        var act = async () => await queryMediator.StreamAsync<GetProductQueryResult>(null!).ToListAsync().ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
