---
name: dev
description: Implement a triaged GitHub issue - refuse one that is not `triaged`, read its handoff and links, work in a fresh git worktree on a branch off the freshly pulled main, targeted tests while working, the full suite before every commit, a draft PR labelled `WIP` until done, then ready to merge without waiting for CI; the worktree stays until the next task replaces it. Use when asked to implement, fix, work on or pick up an issue (`/dev 80`).
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

## 2. A worktree of its own, on a branch off the latest main

Every fix, feature or other piece of work gets a **new git worktree**, so the main checkout stays untouched and tasks never mix.

1. **Clear the previous task's worktree first** (see section 8): `git worktree list`; a worktree of a task that is finished (its PR is ready or merged) is removed now, before the new one is made. One with uncommitted changes is never removed without asking.
2. **Latest main from the remote:** `git fetch origin main` (or `git checkout main && git pull --ff-only origin main` in the main checkout).
3. **Create the worktree with the branch in one go**, from that main, as a sibling folder named after the branch:
   `git worktree add ../<repo>-<kebab> -b fix/<kebab> origin/main` (`feature/<kebab>` for a feature; CLAUDE.md or memory may hold stricter naming; follow them).
4. **Set the worktree up as the repository needs** (CLAUDE.md: e.g. `mise run install`); a worktree has no `node_modules` or build output of its own.
5. **Work only inside it:** run every command from the worktree (`cd` into it) and edit files by their absolute path under it. Never edit the main checkout, never work on `main`.

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

Summarise in chat what was done, what was verified and how, what deviates from the plan, and the PR link, with its state (draft + WIP, or ready). Name the worktree that holds the work.

## 8. The worktree afterwards

- **Finished work stays in its worktree for now.** Do not remove it right after the PR is ready: the user may want to look, and the branch may still get review fixes.
- **Remove it when you start the next issue or task** (section 2, step 1): `git worktree remove ../<repo>-<kebab>` and `git worktree prune`. Only a worktree whose work is finished and committed and pushed; one with uncommitted changes is kept and reported.
- **A request that is something else entirely** (not the next issue: a question, a quick change elsewhere, an unrelated task): **ask the user first** what to do with the worktree, i.e. keep it for later or remove it, before starting on the new request. Never remove it silently.
