---
name: review
description: Review a GitHub pull request - only one that carries neither `reviewing` nor `reviewed`, claim it with the `reviewing` label, review it with the built-in `code-review` skill and post the findings on the PR, then label it `reviewed` and release the claim. Use when asked to review a PR (`/review 99`, "review the next PR"). Never changes the PR's code.
---

# Review a pull request

The argument is a PR number, or `next` (pick the oldest open, non-draft PR that has neither the `reviewing` nor the `reviewed` label; say which one you picked and why). An optional effort level (`low`..`max`) is passed on to `code-review`; without one, `code-review` reuses the level typed last. `ultra` is not available here: it is billed and only the user can launch it.

The findings go **on the PR**, so the author, human or agent, can act on them without this conversation.

## 0. Gate: is it open and free?

`gh pr view N --json state,isDraft,labels,headRefOid`.
- Closed or merged: **stop**, tell the user, do nothing else.
- `reviewing`: another agent is reviewing it right now. **Do not start.** Tell the user and **ask what to do**: take it over, or leave it. Only when the user has already said they know and want it taken over (e.g. "take over the review of #99") is there nothing to ask; say you are taking it over.
- `reviewed`: it has been reviewed already. **Do not review it again** unless the user asks for a new review explicitly (e.g. "review #99 again", after new commits); otherwise tell the user and stop. On a new review, remove `reviewed` when you claim it (step 1).
- A draft (`isDraft`, usually labelled `WIP`) is not finished: `next` never picks one; a named draft is reviewed only after telling the user it is still a draft.

## 1. Claim it

**As soon as the gate passes, before reading the PR:** `gh pr edit N --add-label reviewing` (with `--remove-label reviewed` on a new review), so no other agent picks it up meanwhile. If the label is missing on the repository, create it first: `gh label create reviewing --description "Being reviewed right now by an agent; others skip it unless asked" --color D4C5F9 --force`.

The label comes off again at the end (step 4), after `reviewed` went on. If you abandon the review, remove it too and say so on the PR.

## 2. Read the context

- `gh pr view N --comments`: the description, every comment and review, and the linked issue with its triage handoff (`gh issue view M --comments`): the plan and acceptance criteria say what the PR is meant to do.
- Note the head commit (`headRefOid`); the review is of that commit.
- Check CLAUDE.md for the rules of the areas the diff touches.

## 3. Review

Invoke the built-in `code-review` skill on the PR with its findings posted as inline PR comments: `code-review` with the arguments `N --comment` (plus the effort level, when given). It does the review itself; follow its instructions.

- Add one summary comment on the PR (`gh pr comment N`): the head commit reviewed, how many findings and of what kind, anything checked by hand (and how), and whether you see something blocking. When there are no findings, say so; the comment is what tells the author the review happened.
- **Never change the PR's code** here: no commits, no pushes, no `--fix`. Applying the findings is a separate task (the author's, or `/dev`), unless the user asks for it.

## 4. Bookkeeping

`gh pr edit N --add-label reviewed --remove-label reviewing`: the findings are posted and the claim is released. The PR's other labels (`WIP` and the like) are not yours to change.

## 5. Report

In chat: the PR reviewed (link, head commit), the findings in a few lines (or none), the summary comment's link, and the labels it now carries. No code was changed; say so.

## Rules

- Respect the machine's limits named in CLAUDE.md or memory (e.g. Vitest `--maxWorkers=4`, one `dotnet test` process at a time) for anything you run to check a finding.
- Facts over guesses: a finding without a file:line reference and a concrete failure scenario is marked as uncertain.
- New commits pushed after the review do not take `reviewed` off; a new review only on request (step 0).
