using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.PostgreSql;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.PostgreSql;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlModuleRegistrationTests : LiteBusTestBase, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlModuleRegistrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void AddPostgreSqlCommandInboxStore_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlCommandInboxStore(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ICommandInboxWriter>().Should().BeOfType<PostgreSqlCommandInboxStore>();
        provider.GetRequiredService<ICommandInboxLeaseStore>().Should().BeOfType<PostgreSqlCommandInboxStore>();
        provider.GetRequiredService<ICommandInboxStateStore>().Should().BeOfType<PostgreSqlCommandInboxStore>();
        provider.GetRequiredService<PostgreSqlInboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }

    [Fact]
    public void AddPostgreSqlOutboxStore_ShouldRegisterWriterLeaseAndStateRoles()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions();

        var services = new ServiceCollection();
        services.AddLiteBus(configuration =>
        {
            configuration.AddPostgreSqlOutboxStore(postgres =>
            {
                postgres.UseDataSource(_fixture.DataSource);
                postgres.UseOptions(options);
            });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOutboxMessageWriter>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxMessageLeaseStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<IOutboxMessageStateStore>().Should().BeOfType<PostgreSqlOutboxStore>();
        provider.GetRequiredService<PostgreSqlOutboxStoreRegistration>().Options.TableName.Should().Be(options.TableName);
    }
}
