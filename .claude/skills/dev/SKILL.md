---
name: dev
description: Implement a triaged GitHub issue - refuse one that is not `triaged`, never start one that is `in progress` unless the user asks for it specifically (then continue the ongoing work or restart it, asking when unsure), claim it with the `in progress` label, consult the advisor before every decision, read its handoff and links, work in the main checkout on a branch off the freshly pulled main (no git worktree unless the user asks for one), targeted tests while working, the full suite before every commit, a draft PR labelled `WIP` until done, then ready to merge without waiting for CI and the `in progress` label removed. Use when asked to implement, fix, work on or pick up an issue (`/dev 80`).
---

# Implement an issue

The argument is an issue number.

## 0. Gate: is it triaged, and is it free?

`gh issue view N --json labels,state`.
- If the `triaged` label is missing (or the issue is closed): **stop at once**, tell the user that the issue has not been triaged yet, and offer `/triage N`. Do nothing else.
- If the issue carries `in progress`, someone is implementing it, or was and stopped. **Do not start the implementation** unless the user asks for it specifically (e.g. "take over #80", "continue #80", "restart #80"); a plain `/dev N` is not such a request. Tell the user it is in progress, with what you find of the ongoing work (a branch or draft PR that names the issue: `gh pr list --search "N" --state open`, `git branch -a --list "*<kebab>*"`, the issue's latest comments), and stop.
- When the user does ask, there are two ways in, and the user's words decide between them:
  - **Pick up the ongoing development:** check out its branch (fresh from the remote), read its PR, commits and the comments on the issue, and carry on from there. Section 2's branch creation and 0b's claim are skipped (the label is already on); the clean working tree is still required.
  - **Restart from scratch:** a new branch off the fresh base (section 2), leaving the old branch and PR alone; close the old PR only when the user says so. Say on the issue that the work restarted, and why.
- **If it is not clear which of the two the user wants** (e.g. "take over #80" while a branch with commits exists), **ask the user** before touching anything. Never assume.

## 0b. Claim it

**As soon as the gate passes, before reading or coding:** `gh issue edit N --add-label "in progress"`, so no other agent picks it up meanwhile. The label stays on while the development lasts and **comes off when it is done** (section 6: the PR is ready). If you abandon the work, remove it too and say so on the issue.

## Throughout: the advisor before every decision

During the development, **consult the `advisor` tool before every decision**, and then regularly: before choosing how to read the plan or where it no longer fits (section 1), before the base and the branch (section 2), before each design or implementation choice and each change of approach (section 3), when a test fails in a way you did not expect or you are stuck, and before declaring the work done (section 6). Make the work durable first (files written, a commit when one is due), then ask. Weigh its advice; when it contradicts what you found in the code or the tests, say so in a further call instead of switching silently. If the session has no `advisor` tool, or it fails, tell the user once and go on with your own judgement.

## 1. Read everything

- The issue body and **every comment**: the triage handoff is the specification (findings, code map, plan, acceptance criteria, tests, reproduction).
- Every linked issue, PR and external page (WebFetch), and the files the handoff names.
- Check the code map against current `main`: triage may predate later changes. Note what moved.
- If the plan no longer fits, say so before coding and adjust it; record the deviation in the PR and in a comment on the issue.

## 2. A branch off the latest main, in the main checkout

Every fix, feature or other piece of work gets a **new branch in the main checkout** (the repository folder itself). **No git worktree** (`git worktree add`, `EnterWorktree`) unless the user asks for one: one agent works at a time, and the LSP indexes only the main checkout.

1. **The working tree must be clean:** `git status -sb`. Uncommitted changes (the previous task's or the user's) are never stashed, committed or thrown away on your own: tell the user and ask what to do.
2. **The base, fresh from the remote.** By default the latest `main`: `git fetch origin main`. **When the user names another branch** (a stacked PR built on an open one, a release branch), that branch is the base instead: `git fetch origin <base>`, and the PR targets it. Never choose another base on your own.
3. **Create the branch from that base and switch to it:**
   `git switch -c fix/<kebab> origin/<base>` (`feature/<kebab>` for a feature; CLAUDE.md or memory may hold stricter naming; follow them). Run `mise run install` (CLAUDE.md) when the lockfiles changed since the last install.
4. **Never work on `main`** itself.

## 3. Implement

- Follow the plan and the repository's conventions (CLAUDE.md and the area's file in `.claude/rules/`: layering, i18n, accessibility, docs that must be updated with a behaviour change).
- Add the tests the plan lists; the acceptance criteria are the definition of done. A new or changed user flow always gets its Playwright test in `src/frontend/tankstat-react/e2e` (CLAUDE.md, `.claude/rules/e2e.md`), also when the plan forgot it, and `mise run test:e2e` runs before the commit that adds or changes one.
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
- **Finished** (plan done, acceptance criteria met, tests added, docs updated, full suite green): `gh pr edit N --remove-label WIP`, `gh pr ready N`, and bring the description up to date. **Do not wait for CI**; mark it ready at once. The development is done, so **remove the claim:** `gh issue edit N --remove-label "in progress"`, and comment on the issue with the PR link. A review fix later on (section 8) needs no new claim.
- The description: what changed and why, how it works, the tests run (with figures), what is left out and why, `Closes #N`. Follow CLAUDE.md on attribution lines (e.g. no session link in PR descriptions).

## 7. Report

Summarise in chat what was done, what was verified and how, what deviates from the plan, and the PR link, with its state (draft + WIP, or ready, with the `in progress` label removed from the issue). Name the branch that holds the work.

## 8. Afterwards

- **The checkout stays on the finished branch.** The user may want to look, and the branch may still get review fixes.
- **The next issue or task starts from section 2:** a clean working tree, then a new branch off the fresh base. Only the branch is left behind; there is nothing else to clean up.
