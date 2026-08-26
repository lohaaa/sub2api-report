# syntax=docker/dockerfile:1.7
FROM node:22-alpine AS web-build
WORKDIR /src
RUN corepack enable && corepack prepare pnpm@11.17.0 --activate
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY web/package.json web/package.json
RUN pnpm install --frozen-lockfile --filter @sub2api-report/web...
COPY web ./web
RUN pnpm --filter @sub2api-report/web build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Sub2ApiReport.slnx ./
COPY src ./src
COPY --from=web-build /src/src/Sub2ApiReport.Api/wwwroot ./src/Sub2ApiReport.Api/wwwroot
RUN dotnet restore Sub2ApiReport.slnx
RUN dotnet publish src/Sub2ApiReport.Api/Sub2ApiReport.Api.csproj \
    --configuration Release --no-restore --output /out/api /p:UseAppHost=false
RUN dotnet publish src/Sub2ApiReport.Migrator/Sub2ApiReport.Migrator.csproj \
    --configuration Release --no-restore --output /out/migrator /p:UseAppHost=false
RUN dotnet publish src/Sub2ApiReport.Cli/Sub2ApiReport.Cli.csproj \
    --configuration Release --no-restore --output /out/cli /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/api /app/migrator /app/cli /data \
    && chown -R app:app /app /data
COPY --chmod=0755 deploy/appctl /usr/local/bin/appctl
WORKDIR /app/api
COPY --from=dotnet-build --chown=app:app /out/api ./
COPY --from=dotnet-build --chown=app:app /out/migrator /app/migrator
COPY --from=dotnet-build --chown=app:app /out/cli /app/cli
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "cd /app/migrator && dotnet Sub2ApiReport.Migrator.dll && cd /app/api && exec dotnet Sub2ApiReport.Api.dll"]
