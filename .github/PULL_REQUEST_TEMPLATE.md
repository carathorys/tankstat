<!-- What changes and why. Link the issue: "Closes #N". -->

## Checklist

See [CONTRIBUTING.md](../blob/main/CONTRIBUTING.md). Leave out what does not apply.

- [ ] Tests in every layer the behaviour touches; a new or changed user flow has a real-browser test at phone and desktop size
- [ ] Every new user-visible string in English and Hungarian (or say here which need the Hungarian text)
- [ ] A changed entity or mapping has its migration for all four providers (`mise run db:migration <Name>`)
- [ ] A changed schema or GraphQL document has its generated types committed (`mise run codegen`)
- [ ] The matching file in `docs/` is updated (the CHANGELOG is written at release time, not here)
- [ ] `mise run test` passes locally
