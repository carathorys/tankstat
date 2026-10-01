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
- Domain: fuel log. `Vehicle` and `Refueling` entities have private setters and `Create` factories that enforce the rules (throwing `DomainException`); Application services (`VehicleService`, `RefuelingService`) use repository ports; the GraphQL error filter maps `DomainException` / `NotFoundException` to `VALIDATION_FAILED` / `NOT_FOUND` errors.

## Stack decisions

- **API:** GraphQL only (HotChocolate 16 at `/graphql`); no REST endpoints. Frontend uses Apollo Client 4 and Radix Themes (`@radix-ui/themes`, `@radix-ui/react-form`, icons from `@radix-ui/react-icons`); use the Themes components directly (no custom wrapper layer, no hand-written CSS: use their layout/style props), and keep the app wrapped in `<Theme>` (`main.tsx`, and `renderWithApollo` in tests). jsdom lacks some browser APIs Radix needs; `tests/frontend/setup.ts` stubs them. Radix portals (Select options, dialogs) render outside the component: query with `screen`, not `within`; a Themes Tabs trigger has its label twice in the DOM, so match tab names with a regex.
- **Persistence:** EF Core 10. Provider is chosen at runtime from the `Database` config section: `Provider` (`Sqlite`, `PostgreSql`, `SqlServer`, `MySql`) and `ConnectionString`. With nothing configured it falls back to SQLite (`Data Source=tankstat.db`, working directory); only Sqlite may omit the connection string. MySQL uses Oracle's provider (Pomelo has no EF Core 10 release); MariaDB is not a tested target.
- **Configuration precedence** is standard ASP.NET Core: `appsettings.json` < `appsettings.{Environment}.json` < environment variables (`Database__Provider`, `Database__ConnectionString`) < command line. Environment variables must always be able to override the file; keep new settings in typed, validated options classes bound the same way as `DatabaseOptions`.
- **Migrations:** one project per provider (`src/backend/Tankstat.Migrations.<Provider>`, selected through `MigrationsAssembly`), applied at startup by `DatabaseMigrator`. After changing an entity or its mapping, run `mise run db:migration <Name>` so all four stay in sync (`MigrationsTests` fails otherwise). In-memory SQLite does not work for the app/integration tests because every pooled context gets its own database; use a temp file.
- **Authentication** (`Auth:Mode`: None, Standalone, Oidc, ProxyHeader; default None) is selected lazily at runtime, so all schemes are registered and `ModeAwareSchemeProvider` hides OIDC unless it is the mode. Sign-in only produces a cookie/proxy identity that names a user; `HttpCurrentUser` always reloads the user (admin flag, disabled, session version) from the database. Users live in the `Users` table for every mode. Standalone passwords use the framework PBKDF2 hasher; reset tokens are random, stored hashed, single use. Never expose `User` entities directly in GraphQL (password hash): use the DTOs in `Api/GraphQL/Dtos.cs`.
- **Access layer:** every user-owned entity implements `IOwned`. Use cases never compare owners themselves; they call `AccessService` (`ScopeAsync` for lists, `LevelAsync`/`CanAsync` for single items), and repositories filter lists with `InScope(scope)`. Rules live in the pure `AccessPolicy` (admin/owner full; others get the larger of the instance default and any grant). Invisible items look non-existent (null / `NOT_FOUND`), not `FORBIDDEN`. With `Auth:Mode=None` the actor is `Principal.Anonymous` (full access) and admin features are unavailable.
- **Soft delete:** deletable entities implement `ISoftDeletable` (`DeletedAt`). Vehicles have an EF global query filter hiding trashed rows, so normal queries are safe by default; trash/restore/purge code must use `IgnoreQueryFilters()` (see `VehicleRepository`). Deleting needs Edit access, restoring and emptying the trash apply to what the user may edit; physical deletion (`PurgeAsync`) relies on the database cascade for refuelings. `DeletedAt` is stored as a UTC date-time so comparisons translate on every provider. There is deliberately no scheduled purge job yet (user decision), only the user-initiated "empty trash".
- **UI structure:** `react-router` pages in `src/frontend/pages`, shell (top bar, hamburger menu, health footer) in `src/frontend/shell`; shared bits are only `forms.tsx` (`FieldForm`: text fields on `@radix-ui/react-form` with built-in required/e-mail validation plus busy/error handling) and `messages.tsx` (error/success callouts). Dialogs are uncontrolled where possible: `Dialog.Trigger` / `AlertDialog.Trigger` wrap the button that opens them, no open-state in the page. Tests render with `renderWithApollo(ui, route)` (MemoryRouter) and the in-memory `fakeVehicleBackend` from `tests/frontend/mocks.tsx`; the footer's status badge also has `role="status"`, so query status messages by text.
- **GraphQL codegen (graphql 17):** no hand-written GraphQL types. `schema.graphql` (exported from the API) + `src/frontend/graphql/*.graphql` generate `src/frontend/gql/generated.ts` (typed documents, enums as string unions). After changing the schema or a document run `mise run codegen` and commit both generated files; `SchemaSnapshotTests` and `mise run codegen:check` fail otherwise. Optional grid columns use `@include(if: $withX)` variables so hidden columns are never fetched; because of that a row may lack fields, so anything that edits an entity must load it (see `VehicleDetails`), never trust grid rows.
- **Grids:** sorting/paging/column selection are server-side only (`VehicleQuery`: orderBy/direction/skip/take; count queries for paging). `DataGrid` + `useGridSettings` (localStorage `tankstat.grid.<id>`, validated against current columns) are generic; add a column by adding a `GridColumn` (and the `@include` variable). Repositories always end the order with the id so pages are stable. Queries use `cache-and-network` so every visit refetches.
- **i18n:** every user-visible string goes through `t('...')` with typed keys from `en.json`; add the Hungarian text too (a unit test enforces equal keys/placeholders). Domain/Application errors derive from `KeyedException` (stable `key` + camelCase `args`) and `BusinessErrorFilter` puts them in the GraphQL error extensions; the frontend translates `errors.<key>` (`useErrorText`) and falls back to the server message. New user-facing errors need a key, an English entry and a Hungarian entry. Format dates/numbers with `Intl` (`useFormat`, `Pager`), never by hand.
- **Frontend libraries:** icons from `lucide-react`, animations from `motion` (`motion/react`; keep them short, entry-only, `MotionConfig reducedMotion="user"`). Build mobile-first and check desktop too.
- **Notices:** the server sends public `notices` (currently the `AUTH_DISABLED` warning); the UI renders them generically in `NoticeBanner`.
- HotChocolate schema introspection only works in the Development environment, which is why `scripts/test-api.sh` sets it.

## Testing requirements

Every change must keep all three layers covering behavior, so breaking changes are caught:
- **Unit:** `tests/backend/Tankstat.Application.UnitTests` (pure logic, fakes for ports), `tests/backend/Tankstat.Infrastructure.UnitTests` (config binding, EF adapters on in-memory SQLite), `tests/frontend/*.unit.test.*`.
- **Integration:** `tests/backend/Tankstat.Api.IntegrationTests` (`WebApplicationFactory<Program>`, real DI/GraphQL/EF) and `tests/frontend/*.integration.test.*` (Apollo against msw-mocked GraphQL over HTTP).
- **API:** `tests/backend/Tankstat.Api.ApiTests` are black-box GraphQL contract tests against a running server (`TANKSTAT_API_URL`); `scripts/test-api.sh` starts one on a throwaway port with an in-memory database. A schema change the frontend depends on must fail here.

Frontend test files must be named `*.unit.test.ts(x)` or `*.integration.test.ts(x)` to be picked up by `vite.config.ts`.

## Scope

The repo is a scaffold: besides health, auth and administration, only the vehicle/refueling entities exist (no statistics, editing or deleting yet), and the frontend lists vehicles but has no add-vehicle/log-refuelling forms. The OIDC token-validated provisioning path is covered by unit tests only, not against a real provider. Data created with auth off is owned by the anonymous user (id all zeros) and is only visible to administrators after switching modes. Do not invent domain features or add infrastructure (CI, Docker, codegen) that was not asked for.
