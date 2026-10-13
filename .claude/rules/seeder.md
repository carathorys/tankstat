---
paths:
  - "src/backend/Tankstat.Seeder/**"
  - "tests/backend/Tankstat.Seeder*/**"
---

# The Seeder

- **Seeder:** `src/backend/Tankstat.Seeder` (`mise run seed`) is a test-only standalone tool: it deletes, creates, migrates and seeds a database through the domain factories (so domain rules apply), creates no users, uses the no-auth owner, and refuses unless `Auth:Mode` is None, read as the app reads it (the environment over the settings file: `--settings`, `Settings:Path`). Keep it that way: no users, no auth modes. It asks for confirmation unless `--yes`. It also seeds recurring expenses (mixed states, so the app derives reminders) and access-change notifications for the anonymous user (made-up people, `DataGenerator.Notifications`); each part uses its own `Random` from the seed so adding one does not change the others. New entities/rules: keep the generated data consistent (see `DataGenerator`) and its tests green.
