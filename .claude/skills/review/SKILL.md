---
name: review
description: Review a GitHub pull request - only one that carries neither `reviewing` nor `reviewed`, claim it with the `reviewing` label, review it with the built-in `code-review` skill (its effort levels, `--fix` and `--max-findings` passed on) and post the findings on the PR, then label it `reviewed` and release the claim. Use when asked to review a PR (`/review 99`, `/review next max --fix`, "review the next PR"). Changes the PR's code only with `--fix`, in a worktree of its own.
---

# Review a pull request

## Arguments

`/review <target> [level] [options]`, in any order after the target:

| Argument | Meaning |
| --- | --- |
| `N` | The PR number. |
| `next` | The oldest open, non-draft PR that has neither the `reviewing` nor the `reviewed` label; say which one you picked and why. |
| `low` `medium` `high` `xhigh` `max` | The effort level, passed on to `code-review`. Without one, `code-review` reuses the level typed last. |
| `--max-findings <n>` / `all` / `default` | Passed on to `code-review` as given (it remembers the choice until `default`). |
| `--fix` | After the review, apply the findings to the PR's branch and push them (section 3b). Without it the PR's code is never changed. |
| `--comment` | Accepted and ignored: `/review` always posts the findings on the PR. |

Not available: `ultra` (and with it `--post` / `--no-post`). It is billed and only the user can launch it; if asked for, say so and offer `/code-review ultra N` for them to type. Any other argument: ask what is meant instead of guessing.

The findings go **on the PR**, so the author, human or agent, can act on them without this conversation.

## 0. Gate: is it open and free?

`gh pr view N --json state,isDraft,labels,headRefOid,headRefName,isCrossRepository`.
- Closed or merged: **stop**, tell the user, do nothing else.
- `reviewing`: another agent is reviewing it right now. **Do not start.** Tell the user and **ask what to do**: take it over, or leave it. Only when the user has already said they know and want it taken over (e.g. "take over the review of #99") is there nothing to ask; say you are taking it over.
- `reviewed`: it has been reviewed already. **Do not review it again** unless the user asks for a new review explicitly (e.g. "review #99 again", after new commits); otherwise tell the user and stop. On a new review, remove `reviewed` when you claim it (step 1).
- A draft (`isDraft`, usually labelled `WIP`) is not finished: `next` never picks one; a named draft is reviewed only after telling the user it is still a draft.
- `--fix` on a PR from a fork (`isCrossRepository`): its branch is not ours to push to. Tell the user and ask whether to review it without `--fix`.

## 1. Claim it

**As soon as the gate passes, before reading the PR:** `gh pr edit N --add-label reviewing` (with `--remove-label reviewed` on a new review), so no other agent picks it up meanwhile. If the label is missing on the repository, create it first: `gh label create reviewing --description "Being reviewed right now by an agent; others skip it unless asked" --color D4C5F9 --force`.

The label comes off again at the end (step 4), after `reviewed` went on. If you abandon the review, remove it too and say so on the PR.

## 2. Read the context

- `gh pr view N --comments`: the description, every comment and review, and the linked issue with its triage handoff (`gh issue view M --comments`): the plan and acceptance criteria say what the PR is meant to do.
- Note the head commit (`headRefOid`); the review is of that commit.
- Check CLAUDE.md for the rules of the areas the diff touches.

## 3. Review

Invoke the built-in `code-review` skill on the PR with its findings posted as inline PR comments: `code-review` with the arguments `N --comment`, plus the level, `--max-findings` and `--fix` exactly as the user gave them. It does the review itself; follow its instructions. With `--fix`, set up the worktree of section 3b **first** and invoke it from there, so the fixes land in the PR's branch and nowhere else.

Then add one summary comment on the PR (`gh pr comment N`): the head commit reviewed, the level, how many findings and of what kind, anything checked by hand (and how), whether you see something blocking, and with `--fix` which findings were fixed (the commits) and which were left and why. When there are no findings, say so; the comment is what tells the author the review happened.

Without `--fix`, **never change the PR's code**: no commits, no pushes. Applying the findings is then a separate task (the author's, or `/dev`).

## 3b. `--fix`: a worktree on the PR's branch

1. **Clear the previous task's finished worktree first**, as `/dev` does (`git worktree list`; one with uncommitted changes is never removed without asking).
2. `git fetch origin <headRefName>` and check that `origin/<headRefName>` is the `headRefOid` noted in step 2; if it moved, the review is of the new head: say so and note it again.
3. **A local branch of its own**, because the PR's branch is often checked out in the author's worktree already and git refuses a second checkout: `git worktree add ../<repo>-review-<N> -b review/<N> origin/<headRefName>`. Switch the session into it (`EnterWorktree` with that `path`), set it up as CLAUDE.md says (`mise run install`), and work only inside it.
4. After `code-review` applied its fixes: run the affected tests, then **the full suite before committing** (`mise run test`, within the machine's limits). Red means fix or take the change back; never commit failing tests.
5. Commit in logical commits (CLAUDE.md / memory: split, trailers), staged by hand, never `git add -A`.
6. **Push to the PR's branch without force:** `git push origin HEAD:<headRefName>`. A rejected push means someone pushed meanwhile: stop, do not force, do not rebase on your own; tell the user and leave the commits in the worktree.
7. Leave the worktree (`ExitWorktree` with `keep`). It stays until the next task replaces it (step 1), like `/dev`'s.

## 4. Bookkeeping

`gh pr edit N --add-label reviewed --remove-label reviewing`: the findings are posted (and with `--fix` the fixes pushed) and the claim is released. The PR's other labels (`WIP` and the like) are not yours to change.

## 5. Report

In chat: the PR reviewed (link, head commit, level), the findings in a few lines (or none), the summary comment's link, and the labels it now carries. Without `--fix`: no code was changed; say so. With `--fix`: what was fixed and pushed (commits), what was left, the full suite's result, and the worktree that holds the work.

## Rules

- Respect the machine's limits named in CLAUDE.md or memory (e.g. Vitest `--maxWorkers=4`, one `dotnet test` process at a time) for anything you run to check a finding.
- Facts over guesses: a finding without a file:line reference and a concrete failure scenario is marked as uncertain.
- New commits pushed after the review do not take `reviewed` off; a new review only on request (step 0).
