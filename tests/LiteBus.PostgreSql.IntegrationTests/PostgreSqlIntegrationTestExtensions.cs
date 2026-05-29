using Xunit;

namespace LiteBus.PostgreSql.IntegrationTests;

internal static class PostgreSqlIntegrationTestExtensions
{
    public static void RequireDocker(this PostgreSqlFixture fixture)
    {
        Skip.IfNot(fixture.IsDockerAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }
}
