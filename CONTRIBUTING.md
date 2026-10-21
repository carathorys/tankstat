# Contributing

Thank you for considering a contribution. This page says how to get started, what a change needs, and how work is tracked here. The toolchain, the layout and the test layers are described in [Development](docs/development.md); the conventions of the code are in [CLAUDE.md](CLAUDE.md) and [`.claude/rules/`](.claude/rules/) (written for coding agents, but they apply to people too).

## Before you start

- **A bug**: open an [issue](https://github.com/carathorys/tankstat/issues/new/choose) with the steps, what you expected, the version (shown in the footer), how the app runs (the Docker image or a published build), the database provider and the sign-in mode. Remove secrets and personal data from logs before pasting them.
- **A feature or a change of behaviour**: open an issue or a [discussion](https://github.com/carathorys/tankstat/discussions) first, so the scope is agreed before the work. The app deliberately stays a fuel log (see *Scope* in [CLAUDE.md](CLAUDE.md#scope)): not every idea fits, and it is better to hear that before a pull request.
- **A question**: [Discussions](https://github.com/carathorys/tankstat/discussions), under Q&A.
- **A security problem**: not an issue, see [SECURITY.md](SECURITY.md).

Small fixes (a typo, a wrong link, a broken translation) need no issue: open the pull request.

## Setting up

[mise](https://mise.jdx.dev) installs the toolchain (Node LTS and .NET 10):

```sh
mise install && mise run install   # toolchain, npm ci, dotnet restore
mise run dev:api                   # API with hot reload on http://localhost:5080
mise run dev:web                   # Vite dev server, proxying to the API
mise run seed                      # test data into the dev:api database
mise run test                      # typecheck, lint, every test layer
mise run test:e2e                  # the real-browser tests (Playwright, Docker)
```

See [Development](docs/development.md) for the layout, running single tests, migrations, the seeder and CI.

## What a change needs

- **Tests in every layer the behaviour touches**: backend unit and integration tests, frontend unit and integration tests, and the API contract tests when the GraphQL schema changes. A new or changed user flow (something a person does across pages or dialogs) gets a real-browser test in `src/frontend/tankstat-react/e2e/`, at phone and desktop size.
- **Both languages**: every user-visible string goes through `t('...')`, with an English and a Hungarian entry (a test enforces equal keys). If you cannot write the Hungarian text, say so in the pull request; the maintainer fills it in.
- **Accessibility**: labelled controls, keyboard operation, 44 px touch targets, no information by colour alone; the axe tests in `src/frontend/tankstat-react/tests/app/Accessibility*.integration.test.tsx` cover new screens.
- **Migrations for every provider**: after changing an entity or its mapping, `mise run db:migration <Name>` writes the migration for SQLite, PostgreSQL, SQL Server and MySQL at once; commit all four.
- **Generated GraphQL types committed**: after changing the schema or a `.graphql` document, `mise run codegen` and commit `schema.graphql` and the generated types.
- **Documentation**: a change that alters behaviour or a setting updates the matching file in [`docs/`](docs/) (and the README only when a feature's one-line summary changes). The CHANGELOG is written by the maintainer at release time: do not edit it.
- `mise run test` passes locally before the pull request is opened, or marked ready.

## Pull requests

- One topic per pull request, on a branch named `feature/<topic>` or `fix/<topic>`, off the current `main`.
- Keep it a draft while you are working on it; mark it ready when the checks above are done. The description says what changes and why, and links the issue.
- CI runs the whole suite on every pull request; the real-browser tests run against the published build. A pull request is merged by the maintainer after review.
- Commits are squashed or kept as they are at the maintainer's choice, so their messages need not be polished, but the pull request description should be.

## How work is tracked here

The maintainer runs part of the issue work through coding agents (Claude Code sessions, with the skills in [`.claude/skills/`](.claude/skills/)). Their labels are visible in the tracker and mean:

| Label | Meaning |
| --- | --- |
| `triaging` | An agent is analysing the issue right now. |
| `triaged` | The issue carries an analysis and a plan (the handoff comment), ready for anyone to pick up. |
| `in progress` | Someone is implementing it. Comment on the issue before starting on one of these. |
| `WIP` | The pull request is a draft and not ready to merge. |
| `reviewing` / `reviewed` | An agent is reviewing the pull request / has reviewed it and its findings are applied or answered. |
| `good first issue` / `help wanted` | Scoped work that is a good place to start, and work the maintainer would welcome help with. |

Pull requests by people are welcome and are reviewed by the maintainer. An automated review comment runs on pull requests from branches in this repository only, not on forks: a fork pull request gets the human review, and the maintainer can ask the bot by commenting `@claude` on it. Agents' instructions ([CLAUDE.md](CLAUDE.md), [AGENTS.md](AGENTS.md), [`.claude/rules/`](.claude/rules/)) are the project's conventions; whether you work with an agent or without one, the pull request is judged the same way.

## Good places to start

Look for [`good first issue`](https://github.com/carathorys/tankstat/labels/good%20first%20issue) and [`help wanted`](https://github.com/carathorys/tankstat/labels/help%20wanted). Work that is always welcome: an import from another fuel-log app (see `src/backend/Tankstat.Application/Imports/` and [Importing](docs/importing.md)), a third language (`en.json` and `hu.json` under `src/frontend/tankstat-react/src/i18n/locales/`), Docker Compose and reverse-proxy examples for other setups, and reports of what breaks on a database provider or a browser you use.
