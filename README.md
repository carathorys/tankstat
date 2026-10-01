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
src/backend/Tankstat.Domain/              entities (Vehicle, Refueling, User, access grants) and their rules, no dependencies
src/backend/Tankstat.Infrastructure/      EF Core mapping, repositories, database provider selection
src/backend/Tankstat.Migrations.*/        one EF Core migration set per provider (Sqlite, PostgreSql, SqlServer, MySql)
src/frontend/                             React + Vite app: top bar, hamburger menu, pages (vehicles, trash, account, administration); UI built with Radix Themes (@radix-ui/themes), lucide-react icons, motion animations, react-i18next
tests/backend/Tankstat.Domain.UnitTests/          entity rules
tests/backend/Tankstat.Application.UnitTests/     services (incl. access and auth rules) against in-memory fakes
tests/backend/Tankstat.Infrastructure.UnitTests/  config binding, repositories (SQLite file), migration/model consistency for every provider
tests/backend/Tankstat.Api.IntegrationTests/      in-process GraphQL tests for every auth mode (WebApplicationFactory)
tests/backend/Tankstat.Api.ApiTests/              black-box contract tests against a running server
tests/frontend/                           Vitest unit (*.unit.test.*) and integration (*.integration.test.*) tests
scripts/test-api.sh                       starts the API, runs the API tests, stops it
```

Backend dependencies point inward: Api -> Application, Infrastructure; Infrastructure -> Application; Application -> Domain. Application defines interfaces (ports) that Infrastructure implements, so Application and Domain never reference EF Core or HotChocolate.

The frontend tooling (`package.json`, `vite.config.ts`, `tsconfig*`) lives at the repo root so that both `src/frontend` and `tests/frontend` resolve the same `node_modules`.

## Configuration

The database is chosen through the `Database` section. If nothing is configured, the app falls back to a SQLite file, `tankstat.db`, in the working directory. Values can be set in `appsettings.json` or overridden by environment variables, which take precedence (standard ASP.NET Core order: appsettings.json < appsettings.{Environment}.json < environment variables < command line).

| Setting | Environment variable | Values |
| --- | --- | --- |
| `Database:Provider` | `Database__Provider` | `Sqlite`, `PostgreSql`, `SqlServer`, `MySql` (Oracle provider; MariaDB is not a tested target) |
| `Database:ConnectionString` | `Database__ConnectionString` | provider-specific connection string |

```sh
Database__Provider=PostgreSql Database__ConnectionString="Host=db;Database=tankstat;Username=app;Password=..." \
  dotnet out/Tankstat.Api.dll
```

The connection string may be omitted only for `Sqlite`. The app refuses to start if it is missing for another provider, or if the provider is unknown.

## Authentication and access control

`Auth:Mode` selects how users are authenticated (`Auth__Mode` as an environment variable; environment variables override `appsettings.json`). The default is `None`.

| Mode | Who authenticates | Notes |
| --- | --- | --- |
| `None` | nobody | **Unsafe.** Everyone sees and changes everything; the UI shows a warning. |
| `Standalone` | the app | Login, password change, e-mail reset links, lockout after repeated failures, user management by administrators. |
| `Oidc` | an OpenID Connect provider | Server-side authorization-code flow with PKCE; the browser only gets an HttpOnly session cookie. |
| `ProxyHeader` | a trusted reverse proxy | Authelia, Authentik, Cloudflare Access, oauth2-proxy... The app trusts a user header, but only from the listed proxy addresses. |

All modes end in the same place: a user record in the database that owns data. Anything else you may want later (API tokens, passkeys) can plug in the same way.

**Standalone.** The first administrator is created at first start from configuration (nothing is exposed over HTTP); startup fails if no administrator exists and none is configured. Administrators create users in the UI, which issues a one-time setup link: e-mailed when `Smtp` is configured, otherwise shown to the administrator to hand over. Users can also request a reset link themselves (always answered the same way, so accounts cannot be discovered). Passwords need at least 10 characters; changing or resetting one signs out every other session.

```sh
Auth__Mode=Standalone
Auth__Standalone__AdminEmail=admin@example.com      # first start only
Auth__Standalone__AdminPassword=...                 # first start only
Auth__PublicUrl=https://tankstat.example.com        # used in e-mailed links (required with Smtp)
Smtp__Host=smtp.example.com  Smtp__Port=587  Smtp__Username=...  Smtp__Password=...  Smtp__From=tankstat@example.com
```

**Oidc.** Register `https://<your-host>/auth/oidc/callback` as redirect URI.

```sh
Auth__Mode=Oidc
Auth__Oidc__Authority=https://id.example.com
Auth__Oidc__ClientId=tankstat
Auth__Oidc__ClientSecret=...
Auth__AdminEmails__0=you@example.com                # made administrator on login
```

**ProxyHeader.** `TrustedProxies` is required: without it anyone could send the header themselves.

```sh
Auth__Mode=ProxyHeader
Auth__ProxyHeader__TrustedProxies__0=10.0.0.0/8     # IPs or CIDR ranges of the proxy
Auth__ProxyHeader__UserHeader=X-Forwarded-User      # default
Auth__ProxyHeader__EmailHeader=X-Forwarded-Email    # default, optional
Auth__AdminEmails__0=you@example.com                # matched against e-mail or user name
```

**Who may see and change what.** Every vehicle and refuelling belongs to a user. Owners and administrators have full access. An administrator decides what others may do with someone else's data: an instance-wide default (nothing, view, or view and edit) plus per-user grants ("Bob may edit Alice's data"). Everything goes through one access layer (`AccessService`), so new kinds of data only need to implement `IOwned`. Without authentication everyone has full access.

## Using the app

A top bar with a hamburger menu gives access to:

- **Vehicles**: a grid with add, edit and delete (editing and deleting only appear for vehicles you may edit).
- **Trash**: deleting moves a vehicle to the trash (it keeps a deletion timestamp). From there it can be **restored**, or the whole trash can be **emptied**, which permanently deletes the trashed vehicles you have edit access to together with their refuelings. There is no automatic clean-up yet.
- **Account** (change password in Standalone mode) and **Administration** (administrators only), plus **Sign out**.

### Grids
Sorting (click a column header), paging and column selection are done by the **server**: the GraphQL query gets `orderBy`, `direction`, `skip`, `take`, and one Boolean variable per optional column that drives `@include`, so hidden columns are not even fetched. The toolbar has a refresh button and a column picker (show/hide and move up/down, which works with touch). Column choices, order, page size and sorting are remembered per grid in the browser. Data is re-fetched whenever a page is opened. The layout is mobile-first: on a phone only the essential columns start visible, and the table scrolls horizontally.

### Languages
English and Hungarian (react-i18next); the language menu in the top bar is available before sign-in, defaults to the browser language and is remembered. Every visible string is in `src/frontend/i18n/locales/<lang>.json` (typed keys; a test checks that all languages have the same messages and placeholders). API errors carry a stable `key` and `args` (e.g. `password.tooShort`, `{min: 10}`) which the UI translates; the API's English message is only a fallback. Dates and numbers use `Intl` for the selected language. To add a language: add `<lang>.json`, list it in `LANGUAGES` (`i18n/index.ts`), and the test tells you what is missing.

### GraphQL types are generated
Nothing GraphQL is typed by hand. `schema.graphql` is exported from the API (`mise run schema:export`) and, with the operations in `src/frontend/graphql/*.graphql`, GraphQL Code Generator writes `src/frontend/gql/generated.ts`. After changing the API schema or a `.graphql` document run `mise run codegen` and commit the results; `mise run test` (and an API test) fail if they are stale.

## Test data (seeder)

`Tankstat.Seeder` is a standalone console tool that does only one thing: **deletes the database, creates it, migrates it and fills it with random, consistent test data**. For the no-authentication setup only: it creates no users (everything belongs to the anonymous owner) and refuses to run when `Auth__Mode` is anything but `None`.

```sh
mise run seed                                                       # 25 vehicles, 20 refuelings each, into the dev:api database
mise run seed -- --vehicles 500 --trashed 40 --refuelings 5-60 --seed 7
mise run seed -- --help
```

| Option | Meaning |
| --- | --- |
| `--vehicles <n>` | vehicles to create (default 25) |
| `--trashed <n>` | additional vehicles that are in the trash (default 0) |
| `--refuelings <n\|a-b>` | refuelings per vehicle, a number or a range (default 20) |
| `--seed <n>` | same seed, same data (default 1234) |
| `--provider`, `--connection` | override the database (otherwise `Database__*` settings; `mise run seed` targets the `dev:api` SQLite file) |
| `--yes` | skip the "type yes" confirmation before the database is deleted |

The fuel logs are consistent: per vehicle, dates and the odometer only increase, the last fill-up is recent, litres follow the distance at a per-vehicle consumption, and prices drift slowly. Trashed vehicles keep their fuel logs.

## Data model and migrations

A vehicle fuel log: `Vehicle` (name, licence plate, fuel type) and `Refueling` (date, litres, total cost, odometer, full tank), exposed via GraphQL queries `vehicles` / `vehicle(id)` and mutations `addVehicle` / `logRefueling`.

Pending migrations are applied automatically at startup. Each provider has its own migration project, so after changing an entity run:

```sh
mise run db:migration AddSomething   # adds the migration to all four providers
```

A unit test fails if any provider's migrations are out of sync with the model.

## Development

Run both in separate terminals:

```sh
mise run dev:api   # API with hot reload on http://localhost:5080 (GraphQL IDE at /graphql)
mise run dev:web   # Vite dev server, proxies /graphql and /auth to :5080
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
