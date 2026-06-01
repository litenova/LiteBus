using LiteBus.Extensions.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Testing;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlSchemaHostingTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaHostingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        await backgroundService.ExecuteAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = false
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        await backgroundService.ExecuteAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task InboxSchemaInitializer_WhenValidationEnabled_ShouldValidateAfterEnsure()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true,
            ValidateSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        var action = async () => await backgroundService.ExecuteAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaInitializer_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildOutboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();

        await backgroundService.ExecuteAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaInitializer_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions() with
        {
            EnsureSchemaCreationOnStartup = false
        };

        await using var provider = BuildOutboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlOutboxSchemaInitializer>();

        await backgroundService.ExecuteAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task SchemaInitializer_SecondRun_ShouldRemainIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options);
        var backgroundService = provider.GetRequiredService<PostgreSqlInboxSchemaInitializer>();

        await backgroundService.ExecuteAsync(CancellationToken.None);
        await backgroundService.ExecuteAsync(CancellationToken.None);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    private ServiceProvider BuildInboxProvider(PostgreSqlInboxStoreOptions options)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlInboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildOutboxProvider(PostgreSqlOutboxStoreOptions options)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStorage(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        return services.BuildServiceProvider();
    }
}
