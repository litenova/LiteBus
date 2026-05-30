# Runs PostgreSQL integration tests with a Docker Desktop-friendly environment.
$ErrorActionPreference = "Stop"

Remove-Item Env:DOCKER_HOST -ErrorAction SilentlyContinue

if (Get-Command docker -ErrorAction SilentlyContinue) {
    docker context use desktop-linux 2>&1 | Out-Null
    docker ps -q 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker CLI cannot reach the daemon. Start Docker Desktop and wait until 'docker ps' succeeds."
    }
}

$project = Join-Path $PSScriptRoot "LiteBus.Storage.PostgreSql.IntegrationTests.csproj"
dotnet test $project --configuration Release @args
