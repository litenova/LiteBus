using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.PostgreSql;
using LiteBus.Inbox.PostgreSql.Extensions.Microsoft.Hosting;
using LiteBus.Outbox.PostgreSql;
using LiteBus.Outbox.PostgreSql.Extensions.Microsoft.Hosting;
using LiteBus.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using LiteBus.Testing;
using Microsoft.Extensions.Hosting;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlSchemaHostingTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlSchemaHostingTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task InboxSchemaHostedService_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options, includeSchemaHosting: true);
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<PostgreSqlInboxSchemaHostedService>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InboxSchemaHostedService_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = false
        };

        await using var provider = BuildInboxProvider(options, includeSchemaHosting: true);
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<PostgreSqlInboxSchemaHostedService>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task InboxSchemaHostedService_WhenValidationEnabled_ShouldValidateAfterEnsure()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true,
            ValidateSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options, includeSchemaHosting: true);
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<PostgreSqlInboxSchemaHostedService>()
            .Single();

        var action = async () => await hostedService.StartAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaHostedService_WhenEnabled_ShouldCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildOutboxProvider(options, includeSchemaHosting: true);
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<PostgreSqlOutboxSchemaHostedService>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OutboxSchemaHostedService_WhenDisabled_ShouldNotCreateSchemaOnStartup()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions() with
        {
            EnsureSchemaCreationOnStartup = false
        };

        await using var provider = BuildOutboxProvider(options, includeSchemaHosting: true);
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<PostgreSqlOutboxSchemaHostedService>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options);
        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>();
    }

    [Fact]
    public async Task SchemaHostedService_SecondStartup_ShouldRemainIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions() with
        {
            EnsureSchemaCreationOnStartup = true
        };

        await using var provider = BuildInboxProvider(options, includeSchemaHosting: true);
        var hostedService = provider.GetServices<IHostedService>()
            .OfType<PostgreSqlInboxSchemaHostedService>()
            .Single();

        await hostedService.StartAsync(CancellationToken.None);
        await hostedService.StartAsync(CancellationToken.None);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options);
    }

    private ServiceProvider BuildInboxProvider(PostgreSqlInboxStoreOptions options, bool includeSchemaHosting)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlCommandInboxStore(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });

            if (includeSchemaHosting)
            {
                configuration.AddPostgreSqlCommandInboxSchemaHosting();
            }
        });

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildOutboxProvider(PostgreSqlOutboxStoreOptions options, bool includeSchemaHosting)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStore(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });

            if (includeSchemaHosting)
            {
                configuration.AddPostgreSqlOutboxSchemaHosting();
            }
        });

        return services.BuildServiceProvider();
    }
}
