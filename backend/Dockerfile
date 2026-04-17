# ─── Stage 1: Build ────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy ALL project files before restore
COPY src/Api/IotDashboard.Api.csproj                            src/Api/
COPY tests/UnitTests/IotDashboard.Tests.UnitTests.csproj        tests/UnitTests/
COPY tests/IntegrationTests/IotDashboard.Tests.IntegrationTests.csproj tests/IntegrationTests/

# Copy ALL source folders so the Compile globs can find them during restore
COPY src/ src/

# Full restore (resolves all transitive packages including Configuration.Binder)
RUN dotnet restore src/Api/IotDashboard.Api.csproj

# Publish (restore already done above, but allow it again to be safe)
WORKDIR /src/src/Api
RUN dotnet publish -c Release -o /app/publish

# ─── Stage 2: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_ENVIRONMENT=Development

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD wget -q --spider http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "IotDashboard.Api.dll"]
