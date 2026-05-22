# syntax=docker/dockerfile:1.7
# ---------------------------------------------------------------------------
# MADAuthor — single-image deploy: .NET 8 API + Angular 19 SPA served from wwwroot.
# Multi-stage: build web, build api, run runtime-only.
# ---------------------------------------------------------------------------

# ---- Stage 1: Angular ------------------------------------------------------
FROM node:22-alpine AS web-build
WORKDIR /src
COPY apps/web/package*.json ./
RUN npm ci --no-audit --no-fund --prefer-offline
COPY apps/web/ ./
# Production build → dist/web/browser/
RUN npx ng build --configuration production

# ---- Stage 2: .NET ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api-build
WORKDIR /src

# Copy csproj files first so `restore` is cached.
COPY apps/api/MadAuthor.sln ./
COPY apps/api/MadAuthor.Api/MadAuthor.Api.csproj                       ./MadAuthor.Api/
COPY apps/api/MadAuthor.Application/MadAuthor.Application.csproj       ./MadAuthor.Application/
COPY apps/api/MadAuthor.Contracts/MadAuthor.Contracts.csproj           ./MadAuthor.Contracts/
COPY apps/api/MadAuthor.Domain/MadAuthor.Domain.csproj                 ./MadAuthor.Domain/
COPY apps/api/MadAuthor.Infrastructure/MadAuthor.Infrastructure.csproj ./MadAuthor.Infrastructure/
COPY apps/api/MadAuthor.Worker/MadAuthor.Worker.csproj                 ./MadAuthor.Worker/
RUN dotnet restore MadAuthor.sln

# Now the rest of the source.
COPY apps/api/ ./
RUN dotnet publish MadAuthor.Api/MadAuthor.Api.csproj \
    -c Release \
    -o /publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Stage 3: runtime ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# .NET application + Angular SPA into wwwroot.
COPY --from=api-build /publish ./
COPY --from=web-build /src/dist/web/browser ./wwwroot

# Persistent file storage path used by IFileStorage. Mounted as a Fly volume
# at the same path in fly.toml so uploads + exports survive redeploys.
RUN mkdir -p /app/storage \
    && mkdir -p /app/logs
ENV STORAGE_LOCAL_ROOT=/app/storage
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# Healthcheck — Fly does its own external probe but this lets `docker run` show status too.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/api/health/live || exit 1

ENTRYPOINT ["dotnet", "MadAuthor.Api.dll"]
