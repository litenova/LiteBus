# Builds LiteBus for Anthropic's OSS Scanner (https://red.anthropic.com/oss-scanner/). The scanner builds this file with
# the repository as the build context and removes network access afterwards, so every package the solution and its unit
# tests need is restored here.
FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# scanner contract: the checkout lives inside the image, at /src
COPY . /src
WORKDIR /src

# The base image may ship a newer SDK feature band than global.json allows, so install exactly the SDK it pins.
RUN curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
    && bash /tmp/dotnet-install.sh --jsonfile /src/global.json --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh

# LiteBus.snk is committed, so this build signs the assemblies with the same identity as the published packages.
RUN dotnet restore LiteBus.slnx \
    && dotnet build LiteBus.slnx --no-restore -c Debug

# Unit tests only: the integration suites start PostgreSQL, SQL Server, MySQL and broker containers through
# Testcontainers, which an offline image cannot run. A failing test does not fail the image.
RUN for project in tests/*.UnitTests/*.csproj; do \
      dotnet test "$project" --no-build -c Debug || echo "WARNING: $project reported failures"; \
    done
