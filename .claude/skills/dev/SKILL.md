---
name: dev
description: Implement a triaged GitHub issue - refuse one that is not `triaged`, read its handoff and links, branch, fix it with targeted tests while working, the full suite before every commit, a draft PR labelled `WIP` until done, then ready to merge without waiting for CI. Use when asked to implement, fix, work on or pick up an issue (`/dev 80`).
---

# Implement an issue

The argument is an issue number.

## 0. Gate: is it triaged?

`gh issue view N --json labels,state`. If the `triaged` label is missing (or the issue is closed): **stop at once**, tell the user that the issue has not been triaged yet, and offer `/triage N`. Do nothing else.

## 1. Read everything

- The issue body and **every comment**: the triage handoff is the specification (findings, code map, plan, acceptance criteria, tests, reproduction).
- Every linked issue, PR and external page (WebFetch), and the files the handoff names.
- Check the code map against current `main`: triage may predate later changes. Note what moved.
- If the plan no longer fits, say so before coding and adjust it; record the deviation in the PR and in a comment on the issue.

## 2. Branch

`git fetch origin` and branch off `origin/main`: `fix/<kebab>` for a bug, `feature/<kebab>` for a feature (CLAUDE.md or memory may hold stricter naming; follow them). Never work on `main`.

## 3. Implement

- Follow the plan and the repository's conventions (CLAUDE.md: layering, i18n, accessibility, docs that must be updated with a behaviour change).
- Add the tests the plan lists; the acceptance criteria are the definition of done.
- **While working, run only the affected tests**: the test files or classes you changed and those covering the code you touched, plus typecheck and lint. Never the full suite between steps.
- Respect the machine's limits named in CLAUDE.md or memory (e.g. Vitest `--maxWorkers=4`, one `dotnet test` process at a time).

## 4. Before every commit and/or push: the full suite

- Run the **whole** test suite (`mise run test`, or its steps by hand when a worker cap is needed) before a commit and before a push. When the push follows the commit right away, run it once, before the commit.
- Red means fix first; never commit or push failing tests. Report the results faithfully.

## 5. Commit and push

- Commit messages in the repository's style and with its trailers (CLAUDE.md / memory). Review what is staged; never `git add -A` blindly.
- Push the branch.

## 6. The pull request

- **Not finished yet** (more commits to come): the PR is a **draft** with the `WIP` label (`gh pr create --draft --label WIP`, or `gh pr ready --undo` + `gh pr edit --add-label WIP` on an existing one). Keep pushing commits to it, each with the full suite before it.
- **Finished** (plan done, acceptance criteria met, tests added, docs updated, full suite green): `gh pr edit N --remove-label WIP`, `gh pr ready N`, and bring the description up to date. **Do not wait for CI**; mark it ready at once.
- The description: what changed and why, how it works, the tests run (with figures), what is left out and why, `Closes #N`. Follow CLAUDE.md on attribution lines (e.g. no session link in PR descriptions).

## 7. Report

Summarise in chat what was done, what was verified and how, what deviates from the plan, and the PR link, with its state (draft + WIP, or ready).
