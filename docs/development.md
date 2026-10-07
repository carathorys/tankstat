# Development

For people who work on Tankstat itself: the toolchain, the layout of the repository, how to run and test it, and the tools around it.

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
src/frontend/                             React + Vite app: top bar, hamburger menu, pages (vehicles, trash, account, administration); UI built with MUI (@mui/material, with MUI X grid, charts and date pickers), lucide-react icons, motion animations, react-i18next
tests/backend/Tankstat.Domain.UnitTests/          entity rules
tests/backend/Tankstat.Application.UnitTests/     services (incl. access and auth rules) against in-memory fakes
tests/backend/Tankstat.Infrastructure.UnitTests/  config binding, repositories (SQLite file), migration/model consistency for every provider
tests/backend/Tankstat.Api.IntegrationTests/      in-process GraphQL tests for every auth mode (WebApplicationFactory)
tests/backend/Tankstat.Api.ApiTests/              black-box contract tests against a running server
tests/frontend/                           Vitest unit (*.unit.test.*) and integration (*.integration.test.*) tests, a folder per feature area
tests/frontend/support/                   shared test helpers: msw handlers (server.ts), fake backends (mocks.tsx), setup
scripts/test-api.sh                       starts the API, runs the API tests, stops it
tests/backend/contracts/openai-compatible/  answers as OpenAI-compatible model servers write them, for the app's adapter tests
```

Photo reading is optional: the app talks to a vision model behind an OpenAI-compatible API through its `IRecognitionProvider` port (see [Reading photos with an OpenAI-compatible model](photo-reading.md)).

Backend dependencies point inward: Api -> Application, Infrastructure; Infrastructure -> Application; Application -> Domain. Application defines interfaces (ports) that Infrastructure implements, so Application and Domain never reference EF Core or HotChocolate.

The frontend tooling (`package.json`, `vite.config.ts`, `tsconfig*`) lives at the repo root so that both `src/frontend` and `tests/frontend` resolve the same `node_modules`.

## Running the app

Run both in separate terminals:

```sh
mise run dev:api   # API with hot reload on http://localhost:5080 (GraphQL IDE at /graphql)
mise run dev:web   # Vite dev server, proxies /graphql, /auth/oidc/, /media and /imports to :5080
```

## Testing

```sh
mise run test              # typecheck, lint, then all test layers
mise run test:unit         # frontend + backend unit tests
mise run test:integration  # frontend (msw-mocked HTTP) + backend (in-process)
mise run test:api          # contract tests against a freshly started server
mise run test:coverage     # unit + integration tests with coverage reports in ./coverage
```

The API tests pin the GraphQL schema the frontend relies on, so a breaking schema change fails them. They read the target from `TANKSTAT_API_URL` (default `http://localhost:5080`); `test:api` sets it for you.

Frontend tests are named `*.unit.test.ts(x)` (a function or component on its own) or `*.integration.test.ts(x)` (the app against msw-mocked GraphQL); other names are not picked up. They are grouped by feature area, unit and integration tests side by side, nested where an area has parts:

```text
tests/frontend/
  support/                   shared helpers: msw default handlers (server.ts), fake backends (mocks.tsx), setup.ts
  app/  app/apollo/  app/i18n/   the shell, error handling, accessibility, Apollo, translations
  auth/  pwa/                sign-in, installing the app
  home/                      the home page and its vehicle cards
  vehicles/                  vehicle list and page; vehicles/logs/, vehicles/recurring/, vehicles/dashboard/ for its tabs
  recognition/  pictures/    photo reading, pictures and uploads
  trash/  grid/  settings/  notifications/  admin/  import/
  ui/                        the theme, forms, dialogs, toast and other shared building blocks
```

A new test goes into the folder of the feature it covers; run one file with `npx vitest run tests/frontend/<area>/<file>`.

## Data model and migrations

A vehicle fuel log: `Vehicle` (name, licence plate, fuel type, units, optional picture) and `Refueling` (date, volume, a `Cost`, an `OdometerReading`, full tank, whether a fill-up before it was missed, optional note, who logged it). `OdometerReading`, `Cost` and the units are shared building blocks meant to be reused by later entities (inspections, service fees). Exposed via GraphQL (`vehicles`, `vehicle(id)`, `refuelings(vehicleId, ...)`, `refuelingTrash`, `vehicleLogAccess`, `shareCandidates`, `logDefaults`; mutations such as `addVehicle`, `logRefueling`, `updateRefueling`, `deleteRefueling`, `restoreRefueling`, `setVehicleLogAccess`, `emptyTrash`, `emptyRefuelingTrash`).

Adds can carry an id chosen by the client (`id` on `AddVehicleInput`, `LogRefuelingInput`, `AddExpenseInput`, `AddRecurringExpenseInput`; `expenseId` on `MarkRecurringExpensesDoneInput`): sending the same add again, after an answer that never arrived, answers with what the first one created instead of creating it twice (an id someone else used is refused with `sync.idTaken`). The app's add dialogs choose one per opening, so pressing Save again after a network error never logs a refuelling twice. Vehicles, refuelings, expenses and recurring expenses also count their saves (`version`, from 1); this is the groundwork for working offline and syncing later, where a change made from an old version is detected instead of silently overwriting what someone else saved.

What a repeated add does, in detail:

- It answers with **what the first save stored**: values sent again are not compared, so an edit made between a lost answer and the retry is not applied (edit the entry afterwards). Steps after the save that may not have finished (attaching the photos still waiting as drafts, taking a photo reading, recalculating the consumption) run again, since they are safe to repeat.
- The id is looked up across the whole instance, so a user who sends an id **someone else** already used gets `sync.idTaken`; that tells them such an id exists. Ids are random UUIDs, so this cannot be used to discover anything; it is the one place where something invisible does not simply look missing.
- *Mark as done* is a repeat only when its expense id is already linked to **every** schedule asked for; an id linked to other schedules is another visit and is refused (`sync.idTaken`). A *Mark as done* without an amount logs no expense and has no id: sending it again starts the same interval again, which changes nothing but the version.
- `expectedVersion` is checked and then written, not atomically yet: two changes from the same version at the same moment can both pass. Nothing sends it yet; it becomes a conditional write before the offline sync relies on it (#41).

Pending migrations are applied automatically at startup. Each provider has its own migration project, so after changing an entity run:

```sh
mise run db:migration AddSomething   # adds the migration to all four providers
```

A unit test fails if any provider's migrations are out of sync with the model.

## GraphQL types are generated

Nothing GraphQL is typed by hand. `schema.graphql` is exported from the API (`mise run schema:export`) and, with the operations in `src/frontend/graphql/*.graphql`, GraphQL Code Generator writes `src/frontend/gql/generated.ts`. After changing the API schema or a `.graphql` document run `mise run codegen` and commit the results; `mise run test` (and an API test) fail if they are stale.

## Import formats

Importing lives in `Application/Imports`: one `IImportParser` per file format turns an uploaded file into an `ImportBatch` (vehicle, fuel logs, expenses, recurring reminders), and `ImportService` keeps the batch in memory for the preview and commits it row by row through the same services the dialogs use, so every rule applies and a bad row is reported rather than fatal. `FuelioCsvParser` is the first format. A new format is a new parser with its own tests, and its name added to `FORMATS` in `src/frontend/pages/ImportPage.tsx` (with the `import.file.formats.<name>` texts in both languages).

## Languages

The UI is in English and Hungarian (react-i18next). Every visible string is in `src/frontend/i18n/locales/<lang>.json` (typed keys; a test checks that all languages have the same messages and placeholders). API errors carry a stable `key` and `args` (e.g. `password.tooShort`, `{min: 10}`) which the UI translates; the API's English message is only a fallback. Dates and numbers use `Intl` for the selected language. To add a language: add `<lang>.json`, list it in `LANGUAGES` (`i18n/index.ts`), and the test tells you what is missing.

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
| `--recurring <n\|a-b>` | recurring expenses per vehicle, at most 5 (default 0-2) |
| `--notifications <n>` | notifications about access changes (default 15) |
| `--seed <n>` | same seed, same data (default 1234) |
| `--provider`, `--connection` | override the database (otherwise `Database__*` settings; `mise run seed` targets the `dev:api` SQLite file) |
| `--uploads <folder>` | the folder of uploaded pictures, which is deleted too because nothing would point to the pictures any more (default: `Storage:Path` if configured) |
| `--yes` | skip the "type yes" confirmation before the database is deleted |

The fuel logs are consistent: per vehicle, dates and the odometer only increase, the last fill-up is recent, litres follow the distance at a per-vehicle consumption (now and then a fill-up was not logged: the odometer runs on and the next log is marked), and prices drift slowly. Trashed vehicles keep their fuel logs.

Recurring expenses (insurance, vignette, inspection, oil change, tyres; the distance ones only on vehicles with a reading) are last done so that some are overdue, some due soon and the rest upcoming; the app turns the first two into reminders when the notifications are looked at, as it always does. The access-change notifications are seeded directly for the anonymous user (there is nobody to share with without authentication), over the last two weeks, about made-up people and the first live vehicles: about a third already read, a few standing for several changes, and one "more changes" digest when there are at least ten.

## Continuous integration

`.github/workflows/ci.yml` runs on every push to `main` and every pull request, installing the toolchain from `mise.toml` so CI uses the same Node and .NET versions as developers. Jobs run in parallel: **frontend** (lint, typecheck, the production build of the web app), **frontend tests** (Vitest with coverage, in three shards on three machines; Codecov merges their uploads), **backend** (build, then the unit and in-process integration test projects side by side), **smoke** (the real app as a process in every authentication mode, plus the black-box API contract tests), **codegen** (`schema.graphql` and the generated GraphQL types are up to date). **build** takes the frontend job's web app (no second `vite build`), publishes the API once (`mise run build:api`, version `0.0.0-dev.<sha>`, or the release's when the release workflow calls CI) and uploads the self-hostable output (`out/`) as an artifact; it does not wait for the tests, the release does. `node_modules` is cached per lockfile and Node version (`.github/actions/node-modules`), so `npm ci` only runs when the lockfile changed. Run the same things locally with `mise run test`.

**Coverage.** The frontend job runs Vitest with V8 coverage (`lcov`) and the backend job runs the unit and integration tests with coverlet (`coverlet.runsettings`: the app's own code, without migrations, the seeder and the tests; Cobertura output). Both upload to [Codecov](https://codecov.io/gh/carathorys/tankstat) under the flags `frontend` and `backend`; `codecov.yml` holds the gates (project coverage may not drop by more than 1% against the base, new and changed lines need 80%) and the pull request comment. The upload needs the Codecov GitHub app and a `CODECOV_TOKEN` repository secret (`release.yml` passes secrets on to `ci.yml`); without the token the step only warns. `mise run test:coverage` writes the same reports to `coverage/` (HTML for the frontend at `coverage/frontend/index.html`).
