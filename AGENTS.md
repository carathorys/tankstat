# Agents

Guidance for coding agents (Cursor, Copilot, Cline, Windsurf, OpenCode, Claude Code and the like) working in this repository.

- Read [CLAUDE.md](CLAUDE.md) first: the stack, the layout and layers, the toolchain (`mise run <task>`), the testing requirements and the project's conventions. It is written for Claude Code but applies to every agent and to people.
- The rules of each area are in [`.claude/rules/`](.claude/rules/), one file per area (fuel log and statistics, expenses and imports, notifications, photos, offline sync, backups, photo reading, the settings file, authentication, setup, UI settings, frontend, logging, PWA, CI and tests, real-browser tests, Docker and release, the seeder). Read the file of the area a change touches before starting, and update it when the change alters what it says.
- The documentation for users and operators is in [`docs/`](docs/), one file per topic; a change that alters behaviour or a setting updates the matching file.
- Run `mise run test` before finishing a change; a change of a user flow also needs a real-browser test (`mise run test:e2e`).
- [CONTRIBUTING.md](CONTRIBUTING.md) says how work is tracked in the issues and what a pull request needs.
