FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e1ffd2a92ae84c1291bc1b6887501f8af98e6331e7af6d4c8d37168c5e87a64c AS build

WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json YahooMailMcp.slnx ./
COPY src/YahooMailMcp.Domain/YahooMailMcp.Domain.csproj src/YahooMailMcp.Domain/packages.lock.json ./src/YahooMailMcp.Domain/
COPY src/YahooMailMcp.Application/YahooMailMcp.Application.csproj src/YahooMailMcp.Application/packages.lock.json ./src/YahooMailMcp.Application/
COPY src/YahooMailMcp.Infrastructure.MailKit/YahooMailMcp.Infrastructure.MailKit.csproj src/YahooMailMcp.Infrastructure.MailKit/packages.lock.json ./src/YahooMailMcp.Infrastructure.MailKit/
COPY src/YahooMailMcp.Mcp/YahooMailMcp.Mcp.csproj src/YahooMailMcp.Mcp/packages.lock.json ./src/YahooMailMcp.Mcp/
COPY src/YahooMailMcp.ServiceDefaults/YahooMailMcp.ServiceDefaults.csproj src/YahooMailMcp.ServiceDefaults/packages.lock.json ./src/YahooMailMcp.ServiceDefaults/
COPY src/YahooMailMcp.Server.Http/YahooMailMcp.Server.Http.csproj src/YahooMailMcp.Server.Http/packages.lock.json ./src/YahooMailMcp.Server.Http/
RUN dotnet restore ./src/YahooMailMcp.Server.Http/YahooMailMcp.Server.Http.csproj --locked-mode

COPY src ./src
RUN dotnet publish ./src/YahooMailMcp.Server.Http/YahooMailMcp.Server.Http.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:f5b3b2e2e548828d50e349726f51a5de001286f02c4bbde77db0dd34eb9f55ff AS final

ARG SOURCE_REVISION=unknown
ARG IMAGE_VERSION=0.0.0-local
ARG SOURCE_REPOSITORY_URL="https://github.com/example/YahooMailMcp"
LABEL org.opencontainers.image.source="${SOURCE_REPOSITORY_URL}" \
      org.opencontainers.image.revision="${SOURCE_REVISION}" \
      org.opencontainers.image.version="${IMAGE_VERSION}" \
      org.opencontainers.image.title="Yahoo Mail MCP"

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "YahooMailMcp.Server.Http.dll"]
