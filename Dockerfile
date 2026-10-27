# syntax=docker/dockerfile:1
FROM node:22-bookworm-slim AS client
WORKDIR /src/src/HomeVault.Client
COPY src/HomeVault.Client/package*.json ./
RUN npm ci
COPY src/HomeVault.Client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY src/HomeVault/HomeVault.csproj src/HomeVault/
RUN dotnet restore src/HomeVault/HomeVault.csproj
COPY src/HomeVault/ src/HomeVault/
COPY --from=client /src/src/HomeVault/wwwroot/app/ src/HomeVault/wwwroot/app/
RUN dotnet publish src/HomeVault/HomeVault.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS final
WORKDIR /app
# QuestPDF uses fontconfig and native fonts when generating insurance reports.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core fonts-noto-cjk curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/App_Data/uploads /app/App_Data/keys /app/logs /home/keys \
    && chown -R app:app /app /home/keys
COPY --from=build --chown=app:app /out/ ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Database__ApplyMigrations=false \
    DataProtection__KeysPath=/app/App_Data/keys
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "HomeVault.dll"]
