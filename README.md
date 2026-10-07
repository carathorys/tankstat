# Tankstat

[![CI](https://github.com/carathorys/tankstat/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/carathorys/tankstat/actions/workflows/ci.yml)
[![Release workflow](https://github.com/carathorys/tankstat/actions/workflows/release.yml/badge.svg)](https://github.com/carathorys/tankstat/actions/workflows/release.yml)
[![codecov](https://codecov.io/gh/carathorys/tankstat/graph/badge.svg)](https://codecov.io/gh/carathorys/tankstat)
[![Latest release](https://img.shields.io/github/v/release/carathorys/tankstat?include_prereleases&sort=semver)](https://github.com/carathorys/tankstat/releases)
[![License: MIT](https://img.shields.io/github/license/carathorys/tankstat)](LICENSE)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Node LTS](https://img.shields.io/badge/Node-LTS-339933?logo=nodedotjs&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-3178C6?logo=typescript&logoColor=white)

A self-hosted fuel log for your vehicles: refuelings, other costs, recurring expenses and what they add up to, for you and the people you share a car with. One container is the whole service, and it installs as an app on phones and desktops.

Under the hood: a React + TypeScript frontend (Vite, MUI) and a .NET 10 backend that talk GraphQL (HotChocolate, Apollo Client), with Entity Framework Core for the database. Kestrel serves both the API and the built frontend.

## Features

- **Vehicles and refuelings**: per-vehicle units and currencies (never converted), odometer checks, full and partial fill-ups, and the fuel consumption between full fill-ups.
- **Expenses**: service, insurance, parking and anything else, with a free-text category and an optional odometer.
- **Recurring expenses**: schedules by time, by distance or whichever comes first, with *due soon* and *overdue* reminders; a service visit marks several of them done at once.
- **Dashboard and charts**: key figures, ready-made charts and your own (metric, grouping, chart type, period), costs always per currency.
- **Photos and photo reading**: up to ten photos per entry, and optionally a vision model behind any OpenAI-compatible API that reads the odometer or the receipt into the form. Off by default; nothing leaves your server unless you point it at a paid API.
- **Notifications**: access changes and due schedules, folded so nobody is flooded.
- **Import** from Fuelio (CSV), with a preview and duplicate handling.
- **Sharing and access control**: owners, administrators, instance-wide defaults, per-user grants, and sharing a single vehicle's logs.
- **Sign-in your way**: none, the app's own accounts, OpenID Connect, or a trusted reverse proxy (Authelia, Authentik, oauth2-proxy, ...).
- **Your database**: SQLite (no setup), PostgreSQL, SQL Server or MySQL.
- **Installable web app** with an offline shell and an update prompt that never reloads on its own.
- **Follows you**: the sidebar, grid columns, language, colour mode and the order of your vehicles are saved with your account.
- **English and Hungarian**, dark and light mode, built for keyboards, screen readers and reduced motion.

## Quick start

```sh
docker run -d --name tankstat -p 8080:8080 -v tankstat-data:/data \
  -e Auth__Mode=Standalone -e Auth__Standalone__AdminEmail=admin@example.com -e Auth__Standalone__AdminPassword='a long password' \
  ghcr.io/carathorys/tankstat:latest
```

Then open http://localhost:8080 and sign in with that e-mail and password. The `/data` volume holds the SQLite database and the uploaded pictures. To build the image yourself instead, run `mise run docker:build` and use `tankstat:local` as the image name.

> **Always set `Auth__Mode`.** The shipped `appsettings.json` runs without authentication: everyone sees and changes everything, and there is no administrator. Put TLS in front of the container, and never commit real credentials.

Everything is configured through environment variables, see [Configuration](docs/configuration.md) (the other databases included) and [Authentication](docs/authentication.md). Running without Docker and the release images are in [Self-hosting](docs/self-hosting.md).

## Documentation

- [User guide](docs/user-guide.md): what is on the screen and how each feature behaves.
- [Importing](docs/importing.md): bringing your logs from Fuelio.
- [Install as an app](docs/install-as-an-app.md): the home screen, offline use and updates.
- [Configuration](docs/configuration.md): database, uploaded files, defaults, notifications and logging.
- [Authentication and access control](docs/authentication.md): the four sign-in modes, every setting, and who may see what.
- [Reading photos with an OpenAI-compatible model](docs/photo-reading.md): setting up a vision model, what it is told, and following a photo through the log.
- [Self-hosting](docs/self-hosting.md): the Docker image, running the published build, releases.
- [Development](docs/development.md): toolchain, layout, running, testing, migrations, test data, CI.

## Development

[mise](https://mise.jdx.dev) installs the toolchain (Node LTS and .NET 10):

```sh
mise install && mise run install   # toolchain, npm ci, dotnet restore
mise run dev:api                   # API with hot reload on http://localhost:5080
mise run dev:web                   # Vite dev server, proxying to the API
mise run test                      # typecheck, lint, every test layer
```

See [Development](docs/development.md) for the layout, the test layers and the tools around them.

## License

[MIT](LICENSE).
