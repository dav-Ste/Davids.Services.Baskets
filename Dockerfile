# Build from workspace parent directory so sibling ../Davids.Shared is in build context:
#   docker build -f Davids.Services.Baskets/Dockerfile -t ghcr.io/davids/davids-services-baskets:1.0.0 .
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /workspace

COPY Davids.Shared/Directory.Build.props Davids.Shared/Directory.Packages.props Davids.Shared/global.json ./Davids.Shared/
COPY Davids.Shared/eng/ ./Davids.Shared/eng/
COPY Davids.Shared/src/Davids.ServiceDefaults/Davids.ServiceDefaults.csproj Davids.Shared/src/Davids.ServiceDefaults/packages.lock.json ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Baskets/Directory.Build.props Davids.Services.Baskets/Directory.Packages.props Davids.Services.Baskets/global.json ./Davids.Services.Baskets/
COPY Davids.Services.Baskets/src/Davids.Services.Baskets.Core/Davids.Services.Baskets.Core.csproj Davids.Services.Baskets/src/Davids.Services.Baskets.Core/packages.lock.json ./Davids.Services.Baskets/src/Davids.Services.Baskets.Core/
COPY Davids.Services.Baskets/src/Davids.Services.Baskets.Application/Davids.Services.Baskets.Application.csproj Davids.Services.Baskets/src/Davids.Services.Baskets.Application/packages.lock.json ./Davids.Services.Baskets/src/Davids.Services.Baskets.Application/
COPY Davids.Services.Baskets/src/Davids.Services.Baskets.Infrastructure/Davids.Services.Baskets.Infrastructure.csproj Davids.Services.Baskets/src/Davids.Services.Baskets.Infrastructure/packages.lock.json ./Davids.Services.Baskets/src/Davids.Services.Baskets.Infrastructure/
COPY Davids.Services.Baskets/src/Davids.Services.Baskets.Api/Davids.Services.Baskets.Api.csproj Davids.Services.Baskets/src/Davids.Services.Baskets.Api/packages.lock.json ./Davids.Services.Baskets/src/Davids.Services.Baskets.Api/

RUN dotnet restore ./Davids.Services.Baskets/src/Davids.Services.Baskets.Api/Davids.Services.Baskets.Api.csproj --locked-mode

COPY Davids.Shared/src/Davids.ServiceDefaults/ ./Davids.Shared/src/Davids.ServiceDefaults/
COPY Davids.Services.Baskets/src/ ./Davids.Services.Baskets/src/

RUN dotnet publish ./Davids.Services.Baskets/src/Davids.Services.Baskets.Api/Davids.Services.Baskets.Api.csproj \
      --no-restore -c Release /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

COPY --from=build --chown=10001:10001 /workspace/Davids.Services.Baskets/artifacts/publish/Davids.Services.Baskets.Api/release .
USER 10001:10001
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["dotnet", "--info"]

ENTRYPOINT ["dotnet", "Davids.Services.Baskets.Api.dll"]
