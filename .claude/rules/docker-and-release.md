---
paths:
  - "Dockerfile"
  - ".dockerignore"
  - ".github/workflows/**"
  - "scripts/docker-smoke.sh"
  - "docs/self-hosting.md"
---

# Docker and the release workflow

- **Docker:** the root `Dockerfile` builds one image two ways on a shared `base` stage (Alpine ASP.NET runtime with ICU, non-root, `/data` volume, GraphQL health check; the version labels come last in each target so `base` stays cached): the default target `runtime` from the sources (Node stage: `vite build` only; SDK stage: RID-specific, framework-dependent `dotnet publish` with `-p:Version`), and `release` from CI's `out/` passed as the named build context `app` (`--build-context app=./out`, replacing the empty `app` stage; the `prebuilt` stage keeps only `unix`, `linux-<arch>` and `linux-musl-<arch>` of `runtimes/`, as a RID-specific publish would; both stages must not fail without the context, since the legacy builder runs every stage). The SPA is copied to `wwwroot` and served by the API. `.dockerignore` excludes tests, so the image build must not need them (that is why it runs `vite build`, not `npm run build`). It builds from the working tree, so uncommitted `appsettings.json` ends up in the image; release builds come from CI checkouts.

- **Release workflow:** `.github/workflows/release.yml` (on a published release `vX.Y.Z[-pre]`, or manually) works out the version (`info` job), calls `ci.yml` with it (which therefore has `workflow_call` and a `version` input), makes the image from CI's artifact (target `release`, nothing compiled again), pushes amd64+arm64 by digest only (provenance/SBOM), pulls it back and runs `scripts/docker-smoke.sh` against it, and only then tags it (`imagetools create`, checked to point at the tested digest); a manual run without *push* builds amd64 into the local daemon and smoke-tests that. The `VERSION` build argument and the app's version come from the tag. Changing the Dockerfile means keeping `docker-smoke.sh` and this workflow in step.
