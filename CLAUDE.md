# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Tankstat is a self-hosted web app: React + TypeScript frontend (Vite) and a .NET 10 backend. Kestrel serves both the API and the built SPA in production. See `README.md` for user-facing docs.

## Toolchain and commands

All tooling is managed by `mise` (`mise.toml`: Node LTS, .NET 10). Prefer `mise run <task>` over calling tools directly.

- `mise run install` - `npm ci` + `dotnet restore`
- `mise run dev:api` / `mise run dev:web` - API on :5080 with hot reload; Vite dev server proxying `/graphql` to it
- `mise run test` - typecheck, lint, then every test layer (run this before finishing a change)
- `mise run test:unit`, `test:integration`, `test:api` - single layers
- `mise run build` - self-hostable output in `out/` (published API + SPA in `wwwroot`)

Single tests:
- Frontend: `npx vitest run tests/frontend/<file>` (add `-t "<name>"` to filter)
- Backend: `dotnet test tests/backend/<Project> --filter "FullyQualifiedName~<Name>"`

## Project structure rules

- All source lives under `src/`, all tests under `tests/`. Each is split into `backend/` and `frontend/`.
- Frontend tooling (`package.json`, `vite.config.ts`, `tsconfig*`) lives at the repo root so `src/frontend` and `tests/frontend` share one `node_modules`. Vite's `root` is `src/frontend`; build output goes to `dist/`.
- Backend follows clean architecture, dependencies pointing inward only: `Api -> Application, Infrastructure`; `Infrastructure -> Application`; `Application -> Domain`. Application and Domain must not reference EF Core or HotChocolate. Application defines ports (interfaces such as `IDatabaseProbe`); Infrastructure implements them. Each layer registers itself through an `AddApplication` / `AddInfrastructure` extension.
- Add new .NET projects to `Tankstat.slnx`.

## Stack decisions

- **API:** GraphQL only (HotChocolate 16 at `/graphql`); no REST endpoints. Frontend uses Apollo Client 4.
- **Persistence:** EF Core 10. Provider is chosen at runtime from the `Database` config section: `Provider` (`Sqlite`, `PostgreSql`, `SqlServer`, `MySql`) and `ConnectionString`. MySQL uses Oracle's provider (Pomelo has no EF Core 10 release); MariaDB is not a tested target.
- **Configuration precedence** is standard ASP.NET Core: `appsettings.json` < `appsettings.{Environment}.json` < environment variables (`Database__Provider`, `Database__ConnectionString`) < command line. Environment variables must always be able to override the file; keep new settings in typed, validated options classes bound the same way as `DatabaseOptions`.
- HotChocolate schema introspection only works in the Development environment, which is why `scripts/test-api.sh` sets it.

## Testing requirements

Every change must keep all three layers covering behavior, so breaking changes are caught:
- **Unit:** `tests/backend/Tankstat.Application.UnitTests` (pure logic, fakes for ports), `tests/backend/Tankstat.Infrastructure.UnitTests` (config binding, EF adapters on in-memory SQLite), `tests/frontend/*.unit.test.*`.
- **Integration:** `tests/backend/Tankstat.Api.IntegrationTests` (`WebApplicationFactory<Program>`, real DI/GraphQL/EF) and `tests/frontend/*.integration.test.*` (Apollo against msw-mocked GraphQL over HTTP).
- **API:** `tests/backend/Tankstat.Api.ApiTests` are black-box GraphQL contract tests against a running server (`TANKSTAT_API_URL`); `scripts/test-api.sh` starts one on a throwaway port with an in-memory database. A schema change the frontend depends on must fail here.

Frontend test files must be named `*.unit.test.ts(x)` or `*.integration.test.ts(x)` to be picked up by `vite.config.ts`.

## Scope

The repo is currently a scaffold: the only feature is the `health` query, `Domain` is empty, `AppDbContext` has no entities, and there are no migrations yet. Do not invent domain features or add infrastructure (CI, Docker, codegen) that was not asked for.
