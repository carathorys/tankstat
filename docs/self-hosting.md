# Self-hosting

For operators: the Docker image, the published build without Docker, and how releases reach the container registry.

## Docker

One image contains everything: the .NET API serves GraphQL and the built web app, so a single container is the whole service. It is built from the `Dockerfile` (multi-stage: Node builds the frontend, the .NET SDK publishes the API, an Alpine ASP.NET runtime runs it as an unprivileged user).

```sh
mise run docker:build            # or: docker build -t tankstat:local --build-arg VERSION=1.2.3 .
docker run -d --name tankstat -p 8080:8080 -v tankstat-data:/data \
  -e Auth__Mode=Standalone -e Auth__Standalone__AdminEmail=admin@example.com -e Auth__Standalone__AdminPassword='a long password' \
  tankstat:local
```

- The app listens on port 8080 and is configured only through environment variables (`Database__*`, `Auth__*`, `Smtp__*`, ...; see [Configuration](configuration.md) and [Authentication](authentication.md)). Without `Auth__Mode` the image uses whatever `appsettings.json` was built in, so always set it explicitly (and never bake credentials into `appsettings.json`).
- SQLite, the uploaded pictures and the keys that protect the sign-in cookies live in the `/data` volume (`Database__ConnectionString=Data Source=/data/tankstat.db`, `Storage__Path=/data/uploads` and `DataProtection__KeysPath=/data/keys` by default); mount a volume to keep your data and keep people signed in across container updates. **Upgrading to the version with access and refresh tokens signs everybody out once**: the keys of older versions were not kept, and older session cookies are replaced by the new pair at the next sign-in. Use `Database__Provider` and `Database__ConnectionString` for PostgreSQL, SQL Server or MySQL instead.
- Migrations are applied when the container starts. The container has a health check (the GraphQL endpoint).
- `VERSION` (build argument) is the version of the web app and of the API, both shown in the footer (the web app's even while the server is out of reach); `REVISION` and `CREATED` become OCI image labels.
- Terminate TLS in front of the container with your reverse proxy (see the [ProxyHeader mode](authentication.md#proxy-header-settings-auth__modeproxyheader) for proxy-based sign-in). Installing the web app on a phone, and the service worker behind it, need that HTTPS; the API serves `/manifest.webmanifest` and `/sw.js` from `wwwroot` with the cache rules described under [Install as an app](install-as-an-app.md), and `scripts/docker-smoke.sh` checks them.
- The image builds for amd64 and arm64 with buildx without emulation (`docker buildx build --platform linux/amd64,linux/arm64 .`).

## Releases (container registry)

`.github/workflows/release.yml` publishes the image to the GitHub Container Registry as `ghcr.io/<owner>/<repo>`. Create a GitHub release with a tag like `v1.2.3` (or `v1.2.3-rc.1`): the workflow first runs the whole CI as a gate (its build job publishes the app once, stamped with the release's version), makes the image from that very build (Dockerfile target `release`: nothing is compiled again), pushes it for amd64 and arm64 with a provenance attestation and an SBOM under its digest only, pulls it back and checks it with `scripts/docker-smoke.sh` (web app, API, version, database, non-root), and only then tags it, so the tags always point at the image that passed. A release `v1.2.3` is tagged `1.2.3`, `1.2` and `1` (`latest` for stable releases; pre-releases only get their full version; `0.x` releases get no bare major tag). "Run workflow" on the Release workflow builds and smoke-tests from any branch and pushes a `dev-<sha>` image only if you tick *push*.

```sh
docker pull ghcr.io/<owner>/<repo>:1.2.3
mise run docker:build && mise run docker:smoke      # the same smoke test, locally
```

The first publish creates the package as private and linked to the repository; change its visibility in the package settings if the image should be public.

## Without Docker

Build the self-hostable output yourself and run it with the .NET runtime (the toolchain comes from [mise](development.md#prerequisites)):

```sh
mise run build
dotnet out/Tankstat.Api.dll --urls http://0.0.0.0:8080
```

`out/` contains the published API with the built frontend in `wwwroot`, including the web app's `manifest.webmanifest`, `sw.js` and `workbox-*.js`. Unknown paths fall back to `index.html` for client-side routing (the service worker does the same in the browser, never for `/graphql`, `/auth`, `/media` or `/imports`); `/assets/*` is sent as immutable, everything with a fixed name as `no-cache`.
