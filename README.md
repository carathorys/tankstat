# Tankstat

Self-hosted web app: a React + TypeScript frontend (Vite) and a .NET 10 backend. They talk GraphQL (HotChocolate on the server, Apollo Client in the browser), and the backend stores data with Entity Framework Core. In production, Kestrel serves both the API and the built frontend.

## Prerequisites

[mise](https://mise.jdx.dev) manages the toolchain (Node LTS and .NET 10, see `mise.toml`).

```sh
mise install
mise run install   # npm ci + dotnet restore
```

## Layout

```
src/backend/Tankstat.Api/                 HTTP host: HotChocolate GraphQL at /graphql, static SPA hosting
src/backend/Tankstat.Application/         use cases and ports (e.g. IDatabaseProbe)
src/backend/Tankstat.Domain/              entities and domain logic (no dependencies)
src/backend/Tankstat.Infrastructure/      EF Core, database provider selection, port implementations
src/frontend/                             React + Vite app
tests/backend/Tankstat.Application.UnitTests/     pure unit tests
tests/backend/Tankstat.Infrastructure.UnitTests/  config binding and EF-backed adapters (SQLite in memory)
tests/backend/Tankstat.Api.IntegrationTests/      in-process GraphQL tests (WebApplicationFactory)
tests/backend/Tankstat.Api.ApiTests/              black-box contract tests against a running server
tests/frontend/                           Vitest unit (*.unit.test.*) and integration (*.integration.test.*) tests
scripts/test-api.sh                       starts the API, runs the API tests, stops it
```

Backend dependencies point inward: Api -> Application, Infrastructure; Infrastructure -> Application; Application -> Domain. Application defines interfaces (ports) that Infrastructure implements, so Application and Domain never reference EF Core or HotChocolate.

The frontend tooling (`package.json`, `vite.config.ts`, `tsconfig*`) lives at the repo root so that both `src/frontend` and `tests/frontend` resolve the same `node_modules`.

## Configuration

The database is chosen through the `Database` section. Values come from `appsettings.json` (default: SQLite file `tankstat.db`) and can be overridden by environment variables, which take precedence (standard ASP.NET Core order: appsettings.json < appsettings.{Environment}.json < environment variables < command line).

| Setting | Environment variable | Values |
| --- | --- | --- |
| `Database:Provider` | `Database__Provider` | `Sqlite`, `PostgreSql`, `SqlServer`, `MySql` (Oracle provider; MariaDB is not a tested target) |
| `Database:ConnectionString` | `Database__ConnectionString` | provider-specific connection string |

```sh
Database__Provider=PostgreSql Database__ConnectionString="Host=db;Database=tankstat;Username=app;Password=..." \
  dotnet out/Tankstat.Api.dll
```

The app refuses to start if either value is missing or the provider is unknown.

## Development

Run both in separate terminals:

```sh
mise run dev:api   # API with hot reload on http://localhost:5080 (GraphQL IDE at /graphql)
mise run dev:web   # Vite dev server, proxies /graphql to :5080
```

## Testing

```sh
mise run test              # typecheck, lint, then all test layers
mise run test:unit         # frontend + backend unit tests
mise run test:integration  # frontend (msw-mocked HTTP) + backend (in-process)
mise run test:api          # contract tests against a freshly started server
```

The API tests pin the GraphQL schema the frontend relies on, so a breaking schema change fails them. They read the target from `TANKSTAT_API_URL` (default `http://localhost:5080`); `test:api` sets it for you.

## Building and self-hosting

```sh
mise run build
dotnet out/Tankstat.Api.dll --urls http://0.0.0.0:8080
```

`out/` contains the published API with the built frontend in `wwwroot`. Unknown paths outside `/graphql` fall back to `index.html` for client-side routing.
