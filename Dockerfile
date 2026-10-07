# syntax=docker/dockerfile:1

# One image with everything: the .NET API serves the GraphQL endpoint and the built React app.
#
#   docker build -t tankstat --build-arg VERSION=1.2.3 .
#   docker run -p 8080:8080 -v tankstat-data:/data tankstat
#
# Multi-architecture builds work with buildx (the build stages run natively on the build machine, never under emulation).

# buildx sets BUILDPLATFORM itself (so the build stages never run under emulation); the default keeps the
# classic builder working too.
ARG BUILDPLATFORM=linux/amd64

# ---- Frontend: build the React app ------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:24-alpine AS frontend
WORKDIR /src

# Dependencies first so they stay cached until the lockfile changes.
COPY package.json package-lock.json ./
RUN npm ci

# `vite build` only: type checking, linting and tests are CI's job (the tests are not part of this build context).
COPY vite.config.ts ./
COPY src/frontend ./src/frontend
RUN npx vite build            # -> /src/dist

# ---- Backend: publish the API ------------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
ARG VERSION=0.0.0-dev
ENV DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1

COPY src/backend ./src/backend
# Framework-dependent, but for the exact target (musl libc, amd64 or arm64): this keeps only the native libraries that
# image needs (SQLite, SQL Server client, ...) instead of every OS/CPU combination. VERSION is what the UI shows as the
# API version. buildx sets TARGETARCH; the classic builder does not, hence the default.
ARG TARGETARCH
RUN case "${TARGETARCH:-amd64}" in arm64) rid=linux-musl-arm64 ;; *) rid=linux-musl-x64 ;; esac \
 && dotnet publish src/backend/Tankstat.Api/Tankstat.Api.csproj \
      --configuration Release \
      --runtime "$rid" --self-contained false \
      --output /app \
      -p:Version=${VERSION} \
      -p:UseAppHost=false

# ---- Runtime -------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime

ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ARG CREATED=unknown
LABEL org.opencontainers.image.title="Tankstat" \
      org.opencontainers.image.description="Self-hosted fuel log: API and web app in one container" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      org.opencontainers.image.created="${CREATED}"

# ICU is needed by the SQL Server client (invariant globalization mode is not supported there); tzdata for time zones.
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=backend /app ./
# The API serves the SPA from wwwroot next to the binaries.
COPY --from=frontend /src/dist ./wwwroot

# The SQLite database, the uploaded pictures and the keys that protect the sign-in cookies live in a volume the unprivileged user can write to. Every setting is a normal ASP.NET Core
# setting and can be overridden at run time (Database__Provider, Database__ConnectionString, Auth__Mode, ...).
ENV ASPNETCORE_URLS=http://+:8080 \
    Database__Provider=Sqlite \
    Database__ConnectionString="Data Source=/data/tankstat.db" \
    Storage__Path=/data/uploads \
    DataProtection__KeysPath=/data/keys
RUN mkdir /data && chown "$APP_UID:$APP_UID" /data
VOLUME /data

USER $APP_UID
EXPOSE 8080

# The GraphQL endpoint answers a trivial query once the app (and its database migrations) are up.
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- --header='Content-Type: application/json' --post-data='{"query":"{ __typename }"}' http://127.0.0.1:8080/graphql >/dev/null || exit 1

ENTRYPOINT ["dotnet", "Tankstat.Api.dll"]
